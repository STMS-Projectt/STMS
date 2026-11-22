using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.Controllers;

// Stand-alone e-guarantee verification page (simulates the issuing bank's portal).
// The QR code on a bid opens /GuaranteeVerify?code=... ; anyone can also type the verification code.
// No sign-in is needed, but nothing is shown without the secret verification code.
[AllowAnonymous]
[EnableRateLimiting("guarantee-verify")]
public class GuaranteeVerifyController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? code)
    {
        var model = new GuaranteeCheckResult { Code = code };
        if (string.IsNullOrWhiteSpace(code)) return View(model);

        model.Searched = true;
        if (!Guid.TryParse(code.Trim(), out var verificationCode)) return View(model);

        var bid = await db.Bids.AsNoTracking()
            .Include(b => b.SupplierProfile)
            .Include(b => b.Tender)
            .Include(b => b.BidDocuments)
            .FirstOrDefaultAsync(b => b.GuaranteeVerificationCode == verificationCode && b.BankName != null);
        if (bid == null) return View(model);

        model.Found = true;
        model.BankName = bid.BankName;
        model.HasDocument = bid.BidDocuments.Any(d => d.DocumentType == BidDocument.BankGuarantee);
        model.Amount = bid.BankGuaranteeAmount;
        model.Currency = bid.Currency;
        model.Expiry = bid.BankGuaranteeExpiry;
        model.Principal = bid.SupplierProfile.CompanyName;
        model.TenderTitle = bid.Tender.Title;
        model.WarrantyMonths = bid.LegalWarrantyMonths;
        model.IsValid = bid.Status != "Withdrawn" && bid.BankGuaranteeExpiry?.Date >= DateTime.UtcNow.Date;
        return View(model);
    }
}

public class GuaranteeCheckResult
{
    public string? Code { get; set; }
    public bool Searched { get; set; }
    public bool Found { get; set; }
    public bool IsValid { get; set; }
    public string? BankName { get; set; }
    public bool HasDocument { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime? Expiry { get; set; }
    public string? Principal { get; set; }
    public string? TenderTitle { get; set; }
    public int? WarrantyMonths { get; set; }
}
