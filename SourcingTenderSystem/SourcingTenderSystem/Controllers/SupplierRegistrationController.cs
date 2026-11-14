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
        ? await ResumeApplication() : View(new RegistrationInput());

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Register(RegistrationInput input)
    {
        if (User.Identity?.IsAuthenticated == true) return await ResumeApplication();
        if (!ModelState.IsValid) return View(input);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = input.Email.Trim();
        var user = new IdentityUser { UserName = email, Email = email, PhoneNumber = input.ContactPhone.Trim() };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
            return View(input);
        }

        db.SupplierProfiles.Add(new SupplierProfile
        {
            UserId = user.Id, CompanyName = input.CompanyName.Trim(), TaxNumber = input.TaxNumber.Trim(),
            CommercialRegNo = input.CommercialRegNo.Trim(), ContactPhone = input.ContactPhone.Trim(),
            Address = input.Address.Trim(), Status = SupplierStatus.Pending, CreatedAt = DateTime.UtcNow
        });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(nameof(input.TaxNumber), "This tax number is already registered.");
            return View(input);
        }

        await transaction.CommitAsync();

        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToAction(nameof(UploadDocuments));
    }

    [HttpGet]
    public async Task<IActionResult> RegistrationStatus(bool workspace = false)
    {
        ViewData["ApplicationWorkspace"] = workspace;
        var supplier = await OwnProfile().Include(x => x.SupplierDocuments).AsNoTracking().SingleOrDefaultAsync();
        return supplier is null ? Forbid() : View(supplier);
    }

    [HttpGet]
    public async Task<IActionResult> UploadDocuments()
    {
        var supplier = await OwnProfile().AsNoTracking().SingleOrDefaultAsync();
        if (supplier is null) return Forbid();
        return supplier.Status == SupplierStatus.Pending
            ? await UploadView(new UploadInput()) : RedirectToAction(nameof(RegistrationStatus));
    }

    [HttpPost, RequestSizeLimit(5L * SupplierFileStore.MaxBytes + 65536)]
    public async Task<IActionResult> UploadDocuments(UploadInput input, CancellationToken cancellationToken, bool continueToStatus = false)
    {
        var supplier = await OwnProfile().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (supplier is null) return Forbid();
        if (supplier.Status != SupplierStatus.Pending) return RedirectToAction(nameof(RegistrationStatus));
        if (continueToStatus && input.Files.Count == 0)
        {
            if (await OwnProfile().SelectMany(x => x.SupplierDocuments).AnyAsync(cancellationToken))
                return RedirectToAction(nameof(RegistrationStatus));
            ModelState.AddModelError(nameof(input.Files), "Choose and upload at least one PDF before continuing.");
        }
        if (!SupplierFileStore.DocumentTypes.Contains(input.DocumentType))
            ModelState.AddModelError(nameof(input.DocumentType), "Select a valid document type.");
        if (input.Files.Count is < 1 or > 5)
            ModelState.AddModelError(nameof(input.Files), "Select between 1 and 5 PDF files.");
        if (!ModelState.IsValid) return await UploadView(input);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Lock the application row so a decision and an upload cannot pass the pending check concurrently.
        var matched = await OwnProfile().Where(x => x.Status == SupplierStatus.Pending)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, SupplierStatus.Pending), cancellationToken);
        if (matched == 0) return RedirectToAction(nameof(RegistrationStatus));

        var saved = new List<string>();
        try
        {
            foreach (var file in input.Files)
            {
                var key = await files.SaveAsync(file, cancellationToken);
                if (key is null)
                {
                    foreach (var previous in saved) files.Delete(previous);
                    ModelState.AddModelError(nameof(input.Files), "Each file must be a PDF no larger than 10 MB.");
                    return await UploadView(input);
                }
                saved.Add(key);
                db.SupplierDocuments.Add(new SupplierDocument
                {
                    SupplierProfileId = supplier.Id, DocumentType = input.DocumentType, FilePath = key,
                    ExpiryDate = input.ExpiryDate?.Date, UploadedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            foreach (var key in saved) files.Delete(key);
            throw;
        }
        await transaction.CommitAsync(cancellationToken);

        TempData["Message"] = $"{saved.Count} document(s) uploaded and saved.";
        return RedirectToAction(continueToStatus ? nameof(RegistrationStatus) : nameof(UploadDocuments));
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
    public async Task<IActionResult> FinishRegistration()
    {
        if (!await OwnProfile().AnyAsync()) return Forbid();
        await signIn.SignOutAsync();
        TempData["Message"] = "Your supplier account has been saved. Sign in to continue your application.";
        return RedirectToAction("Index", "Home");
    }
    private async Task<IActionResult> UploadView(UploadInput input)
    {
        ViewData["UploadedCounts"] = await OwnProfile().SelectMany(x => x.SupplierDocuments)
            .GroupBy(x => x.DocumentType).ToDictionaryAsync(x => x.Key, x => x.Count());
        return View("UploadDocuments", input);
    }

    private IQueryable<SupplierProfile> OwnProfile() => db.SupplierProfiles.Where(x => x.UserId == users.GetUserId(User));

    private async Task<IActionResult> ResumeApplication()
    {
        var needsDocuments = await OwnProfile().AnyAsync(x => x.Status == SupplierStatus.Pending && !x.SupplierDocuments.Any());
        return RedirectToAction(needsDocuments ? nameof(UploadDocuments) : nameof(RegistrationStatus));
    }
}




