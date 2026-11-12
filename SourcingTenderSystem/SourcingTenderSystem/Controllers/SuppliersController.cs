using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class SuppliersController(ApplicationDbContext db, UserManager<IdentityUser> users,
    SupplierFileStore files) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? status)
    {
        var query = db.SupplierProfiles.AsNoTracking();
        if (SupplierStatus.All.Contains(status)) query = query.Where(x => x.Status == status);
        ViewBag.Status = status;
        return View(await query.OrderByDescending(x => x.CreatedAt).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var supplier = await db.SupplierProfiles.AsNoTracking().Include(x => x.SupplierDocuments)
            .Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id);
        return supplier is null ? NotFound() : View(supplier);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadDocument(int id)
    {
        var document = await db.SupplierDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (document is null || files.Open(document.FilePath) is not { } stream) return NotFound();
        return File(stream, "application/pdf", $"supplier-document-{id}.pdf");
    }

    [HttpGet] public Task<IActionResult> Approve(int id) => DecisionPageAsync(id, SupplierStatus.Approved, new());
    [HttpGet] public Task<IActionResult> Reject(int id) => DecisionPageAsync(id, SupplierStatus.Rejected, new());
    [HttpGet] public Task<IActionResult> Suspend(int id) => DecisionPageAsync(id, SupplierStatus.Suspended, new());

    [HttpPost] public Task<IActionResult> Approve(int id, DecisionInput input) => DecideAsync(id, SupplierStatus.Approved, input);
    [HttpPost] public Task<IActionResult> Reject(int id, DecisionInput input) => DecideAsync(id, SupplierStatus.Rejected, input);
    [HttpPost] public Task<IActionResult> Suspend(int id, DecisionInput input) => DecideAsync(id, SupplierStatus.Suspended, input);

    private async Task<IActionResult> DecisionPageAsync(int id, string next, DecisionInput input)
    {
        var supplier = await db.SupplierProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (supplier is null) return NotFound();
        ViewBag.Supplier = supplier;
        ViewBag.Decision = next;
        return View("Decision", input);
    }

    private async Task<IActionResult> DecideAsync(int id, string next, DecisionInput input)
    {
        if (!ModelState.IsValid) return await DecisionPageAsync(id, next, input);

        var supplier = await db.SupplierProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (supplier is null) return NotFound();
        var previous = supplier.Status;
        var allowed = next switch
        {
            SupplierStatus.Approved => previous is SupplierStatus.Pending or SupplierStatus.Suspended,
            SupplierStatus.Rejected => previous == SupplierStatus.Pending,
            SupplierStatus.Suspended => previous == SupplierStatus.Approved,
            _ => false
        };
        if (!allowed) return Conflict("This decision is no longer available for the application.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var reason = input.Reason.Trim();
        var application = db.SupplierProfiles.Where(x => x.Id == id && x.Status == previous);
        if (next == SupplierStatus.Approved)
            application = application.Where(x => x.SupplierDocuments.Any());
        var changed = await application.ExecuteUpdateAsync(update => update
            .SetProperty(x => x.Status, next).SetProperty(x => x.StatusReason, reason));
        if (changed == 0)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", "The application has changed or has no documents. Review its current details before deciding.");
            return await DecisionPageAsync(id, next, input);
        }

        var user = await users.FindByIdAsync(supplier.UserId);
        if (user is null) return NotFound();
        var hasRole = await users.IsInRoleAsync(user, Roles.Supplier);
        var result = (next == SupplierStatus.Approved, hasRole) switch
        {
            (true, false) => await users.AddToRoleAsync(user, Roles.Supplier),
            (false, true) => await users.RemoveFromRoleAsync(user, Roles.Supplier),
            _ => IdentityResult.Success
        };
        if (result.Succeeded && (next == SupplierStatus.Approved) != hasRole)
            result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return await DecisionPageAsync(id, next, input);
        }

        var actor = users.GetUserId(User);
        db.AuditLogs.Add(new AuditLog
        {
            EmployeeProfileId = await db.EmployeeProfiles.Where(x => x.UserId == actor).Select(x => (int?)x.Id).SingleOrDefaultAsync(),
            SupplierProfileId = id, EntityName = nameof(SupplierProfile), EntityId = id,
            Action = next, Details = reason, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Message"] = $"Supplier status updated to {next}.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
