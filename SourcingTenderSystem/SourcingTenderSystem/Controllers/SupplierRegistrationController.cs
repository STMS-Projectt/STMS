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
    SupplierFileStore files) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Register() => User.Identity?.IsAuthenticated == true
        ? RedirectToAction(nameof(RegistrationStatus)) : View(new RegistrationInput());

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Register(RegistrationInput input)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(RegistrationStatus));
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

        return RedirectToAction("Login", "Account");
    }

    [HttpGet]
    public async Task<IActionResult> RegistrationStatus()
    {
        var supplier = await OwnProfile().Include(x => x.SupplierDocuments).AsNoTracking().SingleOrDefaultAsync();
        return supplier is null ? Forbid() : View(supplier);
    }

    [HttpGet]
    public async Task<IActionResult> UploadDocuments()
    {
        var supplier = await OwnProfile().AsNoTracking().SingleOrDefaultAsync();
        if (supplier is null) return Forbid();
        return supplier.Status == SupplierStatus.Pending
            ? View(new UploadInput()) : RedirectToAction(nameof(RegistrationStatus));
    }

    [HttpPost, RequestSizeLimit(SupplierFileStore.MaxBytes + 65536)]
    public async Task<IActionResult> UploadDocuments(UploadInput input, CancellationToken cancellationToken)
    {
        var supplier = await OwnProfile().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (supplier is null) return Forbid();
        if (supplier.Status != SupplierStatus.Pending) return RedirectToAction(nameof(RegistrationStatus));
        if (!SupplierFileStore.DocumentTypes.Contains(input.DocumentType))
            ModelState.AddModelError(nameof(input.DocumentType), "Select a valid document type.");
        if (!ModelState.IsValid) return View(input);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Lock the application row so a decision and an upload cannot pass the pending check concurrently.
        var matched = await OwnProfile().Where(x => x.Status == SupplierStatus.Pending)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, SupplierStatus.Pending), cancellationToken);
        if (matched == 0) return RedirectToAction(nameof(RegistrationStatus));

        var key = await files.SaveAsync(input.File, cancellationToken);
        if (key is null)
        {
            ModelState.AddModelError(nameof(input.File), "Choose a PDF file no larger than 10 MB.");
            return View(input);
        }

        db.SupplierDocuments.Add(new SupplierDocument
        {
            SupplierProfileId = supplier.Id, DocumentType = input.DocumentType, FilePath = key,
            ExpiryDate = input.ExpiryDate?.Date, UploadedAt = DateTime.UtcNow
        });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch
        {
            files.Delete(key);
            throw;
        }
        await transaction.CommitAsync(cancellationToken);

        TempData["Message"] = "Document uploaded.";
        return RedirectToAction(nameof(RegistrationStatus));
    }

    [HttpGet]
    public async Task<IActionResult> DownloadDocument(int id)
    {
        var document = await OwnProfile().SelectMany(x => x.SupplierDocuments)
            .AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (document is null || files.Open(document.FilePath) is not { } stream) return NotFound();
        return File(stream, "application/pdf", $"supplier-document-{id}.pdf");
    }

    private IQueryable<SupplierProfile> OwnProfile() => db.SupplierProfiles.Where(x => x.UserId == users.GetUserId(User));
}
