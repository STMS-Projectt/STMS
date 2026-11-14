using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClarificationsController(ApplicationDbContext db, ClarificationRouting routing) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? kind, int? recordId)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        var query = Threads(actor);
        if (recordId is int id)
            query = kind switch {
                "request" => query.Where(r => r.PurchaseRequisitionId == id),
                "tender" => query.Where(r => r.TenderId == id || r.Bid!.TenderId == id),
                "bid" => query.Where(r => r.BidId == id),
                _ => query.Where(r => false)
            };
        var route = kind is not null && recordId is int target ? await routing.Get(kind, target, actor) : null;
        return View(new ClarificationInbox(await query.OrderByDescending(r => r.AnsweredAt ?? r.CreatedAt)
            .ThenByDescending(r => r.Id).ToListAsync(), kind, recordId, route, recordId is null ? await routing.RequestTargets(actor) : []));
    }

    [HttpPost, Authorize(Roles = Roles.SourcingOfficer)]
    public async Task<IActionResult> ClaimRequest(int id)
    {
        var actor = await routing.Current(User);
        if (actor?.Employee is not int officerId) return Forbid();
        var changed = await db.PurchaseRequisitions.Where(r => r.Id == id &&
            r.ReviewedByEmployeeProfileId == null && (r.Status == "Submitted" || r.Status == "Under Review"))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.ReviewedByEmployeeProfileId, officerId)
                .SetProperty(r => r.Status, "Under Review"));
        if (changed == 0)
            TempData["ClarificationError"] = "The request is already assigned or is no longer awaiting review.";
        return RedirectToAction(nameof(Index), new { kind = "request", recordId = id });
    }
    [HttpGet]
    public async Task<IActionResult> Create(string kind, int recordId)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        var route = await routing.Get(kind, recordId, actor);
        return route is null ? NotFound() : Compose(new() { Kind = kind, RecordId = recordId }, route);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ClarificationInput input)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var route = await routing.Get(input.Kind, input.RecordId, actor);
        if (route is null) return NotFound();
        var recipient = route.Recipients.SingleOrDefault(p => p.Key == input.Recipient);
        if (recipient is null) ModelState.AddModelError(nameof(input.Recipient), "Select an eligible recipient.");
        if (!ModelState.IsValid) return Compose(input, route);
        var row = new ClarificationRequest
        {
            SenderEmployeeProfileId = actor.Employee, SenderSupplierProfileId = actor.Supplier,
            ReceiverEmployeeProfileId = recipient!.Employee, ReceiverSupplierProfileId = recipient.Supplier,
            Subject = input.Subject.Trim(), Description = input.Description.Trim(), Status = "Pending",
            PurchaseRequisitionId = input.Kind == "request" ? input.RecordId : null,
            TenderId = input.Kind == "tender" ? input.RecordId : null,
            BidId = input.Kind == "bid" ? input.RecordId : null, CreatedAt = DateTime.UtcNow
        };
        db.ClarificationRequests.Add(row);
        await db.SaveChangesAsync();
        Notify(recipient.Employee, recipient.Supplier, "New clarification request", row);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = "Clarification sent.";
        return RedirectToAction(nameof(Details), new { id = row.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        var row = await Threads(actor).SingleOrDefaultAsync(r => r.Id == id);
        if (row is null) return NotFound();
        return View(new ClarificationThread(row, row.Status == "Pending" && IsReceiver(row, actor) &&
            await CanReply(row, actor), row.Status == "Answered" && IsSender(row, actor)));
    }

    [HttpPost]
    public async Task<IActionResult> Answer(int id, [Required, StringLength(2000)] string response)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var row = await db.ClarificationRequests.SingleOrDefaultAsync(r => r.Id == id);
        if (row is null || !IsReceiver(row, actor)) return NotFound();
        if (row.Status != "Pending" || !await CanReply(row, actor))
            return Conflict("This clarification can no longer be answered at the current stage.");
        if (!ModelState.IsValid)
        {
            TempData["ClarificationError"] = "Enter a response between 1 and 2,000 characters.";
            return RedirectToAction(nameof(Details), new { id });
        }
        row.Response = response.Trim();
        row.AnsweredAt = DateTime.UtcNow;
        row.Status = "Answered";
        Notify(row.SenderEmployeeProfileId, row.SenderSupplierProfileId, "Clarification answered", row);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = "Response sent.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Resolve(int id)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        var query = db.ClarificationRequests.Where(r => r.Id == id && r.Status == "Answered");
        query = actor.Employee is int e ? query.Where(r => r.SenderEmployeeProfileId == e)
            : query.Where(r => r.SenderSupplierProfileId == actor.Supplier);
        var now = DateTime.UtcNow;
        if (await query.ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, "Resolved")
            .SetProperty(r => r.ResolvedAt, now)) == 0) return NotFound();
        TempData["Message"] = "Clarification resolved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private IQueryable<ClarificationRequest> Threads(ClarificationPerson actor)
    {
        var query = db.ClarificationRequests.AsNoTracking();
        query = actor.Employee is int employee
            ? query.Where(r => r.SenderEmployeeProfileId == employee || r.ReceiverEmployeeProfileId == employee)
            : query.Where(r => r.SenderSupplierProfileId == actor.Supplier || r.ReceiverSupplierProfileId == actor.Supplier);
        return query.Include(r => r.SenderEmployeeProfile).Include(r => r.ReceiverEmployeeProfile)
            .Include(r => r.SenderSupplierProfile).Include(r => r.ReceiverSupplierProfile)
            .Include(r => r.PurchaseRequisition).Include(r => r.Tender).Include(r => r.Bid).ThenInclude(b => b!.Tender);
    }

    private async Task<bool> CanReply(ClarificationRequest row, ClarificationPerson actor)
    {
        var kind = row.PurchaseRequisitionId.HasValue ? "request" : row.BidId.HasValue ? "bid" : "tender";
        var id = row.PurchaseRequisitionId ?? row.BidId ?? row.TenderId;
        if (id is null) return false;
        var route = await routing.Get(kind, id.Value, actor);
        var sender = new ClarificationPerson(row.SenderEmployeeProfileId, row.SenderSupplierProfileId, "");
        return route?.Recipients.Any(p => p.Key == sender.Key) == true;
    }

    private static bool IsReceiver(ClarificationRequest row, ClarificationPerson person) =>
        person.Employee is int e ? row.ReceiverEmployeeProfileId == e : row.ReceiverSupplierProfileId == person.Supplier;
    private static bool IsSender(ClarificationRequest row, ClarificationPerson person) =>
        person.Employee is int e ? row.SenderEmployeeProfileId == e : row.SenderSupplierProfileId == person.Supplier;

    private IActionResult Compose(ClarificationInput input, ClarificationRoute route)
    {
        ViewData["ClarificationRoute"] = route;
        return View("Create", input);
    }

    private void Notify(int? employee, int? supplier, string title, ClarificationRequest row) =>
        db.Notifications.Add(new Notification { EmployeeProfileId = employee, SupplierProfileId = supplier,
            Title = title, Message = row.Subject, Link = Url.Action(nameof(Details), new { id = row.Id }), CreatedAt = DateTime.UtcNow });
}
