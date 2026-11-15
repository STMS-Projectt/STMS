using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize]
public sealed class SupplierRegistrationController(ApplicationDbContext db, UserManager<IdentityUser> users,
    SupplierFileStore files, SignInManager<IdentityUser> signIn) : Controller
{
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Register() => User.Identity?.IsAuthenticated == true
        ? await ResumeApplication() : await RegistrationView(new RegistrationInput());

    private async Task<IActionResult> RegistrationView(RegistrationInput input)
    {
        ViewBag.Categories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        return View("Register", input);
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Register(RegistrationInput input)
    {
        if (User.Identity?.IsAuthenticated == true) return await ResumeApplication();
        var ids = input.CategoryIds.Distinct().ToArray();
        var categories = await db.Categories.Where(c => ids.Contains(c.Id)).ToListAsync();
        if (ids.Length == 0 || categories.Count != ids.Length)
            ModelState.AddModelError(nameof(input.CategoryIds), "Select at least one valid category.");
        if (!ModelState.IsValid) return await RegistrationView(input);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = input.Email.Trim();
        var user = new IdentityUser { UserName = email, Email = email, PhoneNumber = input.ContactPhone.Trim() };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return await RegistrationView(input);
        }

        db.SupplierProfiles.Add(new SupplierProfile
        {
            UserId = user.Id, CompanyName = input.CompanyName.Trim(), TaxNumber = input.TaxNumber.Trim(),
            CommercialRegNo = input.CommercialRegNo.Trim(), ContactPhone = input.ContactPhone.Trim(),
            Address = input.Address.Trim(), Status = SupplierStatus.Pending, CreatedAt = DateTime.UtcNow,
            Categories = categories
        });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(nameof(input.TaxNumber), "This tax number is already registered.");
            return await RegistrationView(input);
        }

        await transaction.CommitAsync();

        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToAction(nameof(UploadDocuments));
    }

    [HttpGet]
    public async Task<IActionResult> RegistrationStatus(bool workspace = false)
    {
        var supplier = await OwnProfile().Include(x => x.SupplierDocuments).Include(x => x.Categories).Include(x => x.User)
            .AsNoTracking().SingleOrDefaultAsync();
        if (supplier is null) return Forbid();
        ViewData["ApplicationWorkspace"] = workspace || supplier.ApplicationSubmittedAt != null || supplier.Status != SupplierStatus.Pending;
        return View(supplier);
    }

    [HttpGet]
    public async Task<IActionResult> UploadDocuments()
    {
        var draft = await Draft().AsNoTracking().Include(x => x.SupplierDocuments).SingleOrDefaultAsync();
        return draft is null ? RedirectToAction(nameof(RegistrationStatus), new { workspace = true }) : View(draft);
    }

    [HttpGet]
    public async Task<IActionResult> AddDocument()
    {
        if (!await Draft().AnyAsync()) return Forbid();
        return View(new UploadInput());
    }

    [HttpPost, RequestSizeLimit(SupplierFileStore.MaxBytes + 65536)]
    public async Task<IActionResult> AddDocument(UploadInput input, CancellationToken cancellationToken)
    {
        var supplierId = await Draft().Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (supplierId is null) return Forbid();
        if (!SupplierFileStore.DocumentTypes.Contains(input.DocumentType))
            ModelState.AddModelError(nameof(input.DocumentType), "Select a valid document type.");
        if (!ModelState.IsValid || input.File is null) return View(input);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Hold the same application row lock used by submission.
        var matched = await Draft()
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, SupplierStatus.Pending), cancellationToken);
        if (matched == 0) return Forbid();
        var saved = await files.SaveAsync(input.File, cancellationToken);
        if (saved is null)
        {
            ModelState.AddModelError(nameof(input.File), "Choose a valid PDF no larger than 10 MB.");
            return View(input);
        }
        try
        {
            db.SupplierDocuments.Add(new SupplierDocument
            {
                SupplierProfileId = supplierId.Value, DocumentType = input.DocumentType, FilePath = saved,
                FileName = SupplierFileStore.SafeName(input.File.FileName), ExpiryDate = input.ExpiryDate?.Date, UploadedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            files.Delete(saved);
            throw;
        }
        TempData["Message"] = "Document added to your application.";
        return RedirectToAction(nameof(UploadDocuments));
    }

    [HttpPost]
    public async Task<IActionResult> RemoveDocument(int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await Draft().ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, SupplierStatus.Pending)) == 0)
            return Forbid();
        var document = await Draft().SelectMany(s => s.SupplierDocuments).SingleOrDefaultAsync(d => d.Id == id);
        if (document is null) return NotFound();
        db.Remove(document);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        files.Delete(document.FilePath);
        TempData["Message"] = "Document removed from the draft.";
        return RedirectToAction(nameof(UploadDocuments));
    }

    [HttpGet]
    public async Task<IActionResult> DownloadDocument(int id)
    {
        var document = await OwnProfile().SelectMany(x => x.SupplierDocuments)
            .AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (document is null || files.Open(document.FilePath) is not { } stream) return NotFound();
        return File(stream, "application/pdf", $"supplier-document-{id}.pdf");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitApplication()
    {
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync();
        if (await Draft().ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, SupplierStatus.Pending)) == 0)
            return Forbid();
        if (await Draft().Where(s => s.SupplierDocuments.Any())
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ApplicationSubmittedAt, now)) == 0)
        {
            TempData["ApplicationError"] = "Add at least one supporting PDF before submitting.";
            return RedirectToAction(nameof(UploadDocuments));
        }
        await tx.CommitAsync();
        TempData["Message"] = "Application submitted for review.";
        return RedirectToAction("Index", "SupplierWorkspace");
    }

    private IQueryable<SupplierProfile> OwnProfile() => db.SupplierProfiles.Where(x => x.UserId == users.GetUserId(User));
    private IQueryable<SupplierProfile> Draft() => OwnProfile().Where(s => s.Status == SupplierStatus.Pending && s.ApplicationSubmittedAt == null);

    private async Task<IActionResult> ResumeApplication()
    {
        var needsDocuments = await Draft().AnyAsync();
        return RedirectToAction(needsDocuments ? nameof(UploadDocuments) : nameof(RegistrationStatus));
    }
}




