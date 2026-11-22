using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;

namespace SourcingTenderSystem.Controllers;

// Stand-alone e-guarantee verification page (simulates the issuing bank's portal).
// The QR code on a bid opens /GuaranteeVerify?code=... ; anyone can also type the guarantee number + code.
// No sign-in is needed, but nothing is shown without the secret verification code.
[AllowAnonymous]
[EnableRateLimiting("guarantee-verify")]
public class GuaranteeVerifyController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? code, string? number)
    {
        var model = new GuaranteeCheckResult { Code = code, Number = number };
        if (string.IsNullOrWhiteSpace(code)) return View(model);

        model.Searched = true;
        if (!Guid.TryParse(code.Trim(), out var verificationCode)) return View(model);

        var bid = await db.Bids.AsNoTracking()
            .Include(b => b.SupplierProfile)
            .Include(b => b.Tender)
            .FirstOrDefaultAsync(b => b.GuaranteeVerificationCode == verificationCode && b.BankGuaranteeNumber != null);
        // When a number is typed it must match too
        if (bid == null || !string.IsNullOrWhiteSpace(number) &&
            !string.Equals(bid.BankGuaranteeNumber, number.Trim(), StringComparison.OrdinalIgnoreCase))
            return View(model);

        model.Found = true;
        model.Number = bid.BankGuaranteeNumber;
        model.BankName = bid.BankName;
        model.Type = bid.BankGuaranteeType;
        model.Amount = bid.BankGuaranteeAmount;
        model.Currency = bid.Currency;
        model.Expiry = bid.BankGuaranteeExpiry;
        model.Principal = bid.SupplierProfile.CompanyName;
        model.TenderTitle = bid.Tender.Title;
        model.WarrantyMonths = bid.LegalWarrantyMonths;
        model.OfferNature = bid.OfferNature;
        model.IsValid = bid.Status != "Withdrawn" && bid.BankGuaranteeExpiry?.Date >= DateTime.UtcNow.Date;
        return View(model);
    }
}

public class GuaranteeCheckResult
{
    public string? Code { get; set; }
    public string? Number { get; set; }
    public bool Searched { get; set; }
    public bool Found { get; set; }
    public bool IsValid { get; set; }
    public string? BankName { get; set; }
    public string? Type { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime? Expiry { get; set; }
    public string? Principal { get; set; }
    public string? TenderTitle { get; set; }
    public int? WarrantyMonths { get; set; }
    public string? OfferNature { get; set; }
}
