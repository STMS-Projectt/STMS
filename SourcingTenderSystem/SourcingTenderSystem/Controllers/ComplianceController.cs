using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

// Sourcing officer: checks the submitted bids of closed tenders (Compliant / Non-Compliant),
// compares the offers side by side, and sends the tender to evaluation.
[Authorize(Roles = "SourcingOfficer")]
public class ComplianceController : Controller
{
    private readonly ApplicationDbContext _context;

    public ComplianceController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Compliance/Pending
    [HttpGet]
    public async Task<IActionResult> Pending()
    {
        var closedTenders = await _context.Tenders
            .Where(t => t.Status == "Closed")
            .OrderBy(t => t.CloseDate)
            .ToListAsync();

        var rows = new List<CompliancePendingRow>();
        foreach (var tender in closedTenders)
        {
            int submitted = await CountBidsAsync(tender.Id, "Submitted");
            int compliant = await CountBidsAsync(tender.Id, "Compliant");
            int nonCompliant = await CountBidsAsync(tender.Id, "Non-Compliant");

            // Skip tenders that have no submitted bids at all (nothing to check)
            if (submitted + compliant + nonCompliant == 0)
            {
                continue;
            }

            rows.Add(new CompliancePendingRow
            {
                Tender = tender,
                SubmittedCount = submitted,
                CompliantCount = compliant,
                NonCompliantCount = nonCompliant,
                ReadyForEvaluation = submitted == 0 && compliant > 0
            });
        }

        return View(rows);
    }

    // GET: /Compliance/Review?tenderId=3   (this is also the "Review & Compare Offers" page)
    [HttpGet]
    public async Task<IActionResult> Review(int tenderId)
    {
        var tender = await _context.Tenders.FindAsync(tenderId);
        if (tender == null)
        {
            return NotFound();
        }

        // Bids are sealed: nobody sees them while the tender is still open
        if (tender.Status == "Open")
        {
            TempData["Error"] = "This tender is still open. Bids can be reviewed only after it is closed.";
            return RedirectToAction(nameof(Pending));
        }

        // All bids except Draft and Withdrawn, lowest total price first
        var bids = await _context.Bids
            .Include(b => b.SupplierProfile)
            .Include(b => b.BidItems)
            .Include(b => b.BidDocuments)
            .Where(b => b.TenderId == tender.Id && b.Status != "Draft" && b.Status != "Withdrawn")
            .OrderBy(b => b.TotalPrice)
            .ToListAsync();

        var model = new ComplianceReviewViewModel();
        model.Tender = tender;
        model.Bids = bids;
        model.RequestedItems = await _context.PurchaseRequisitionItems
            .Where(p => p.PurchaseRequisitionId == tender.PurchaseRequisitionId)
            .OrderBy(p => p.Id)
            .ToListAsync();
        model.TenderIsClosed = tender.Status == "Closed";

        // Count the statuses and check if all bids use the same currency
        foreach (var bid in bids)
        {
            if (bid.Status == "Submitted")
            {
                model.SubmittedCount++;
            }
            if (bid.Status == "Compliant")
            {
                model.CompliantCount++;
            }
            if (bid.Currency != bids[0].Currency)
            {
                model.MixedCurrencies = true;
            }
        }

        model.CanSendToEvaluation = model.TenderIsClosed
                                    && model.SubmittedCount == 0
                                    && model.CompliantCount > 0;

        return View(model);
    }

    // POST: /Compliance/MarkCompliant/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkCompliant(int id)
    {
        var bid = await GetBidAsync(id);
        if (bid == null)
        {
            return NotFound();
        }

        string? error = CheckCanDecide(bid);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Review), new { tenderId = bid.TenderId });
        }

        bid.Status = "Compliant";
        await _context.SaveChangesAsync();

        // TODO: audit log + notification (team mechanism) - bid marked compliant

        TempData["Msg"] = $"The bid of {bid.SupplierProfile.CompanyName} is now Compliant.";
        return RedirectToAction(nameof(Review), new { tenderId = bid.TenderId });
    }

    // GET: /Compliance/MarkNonCompliant/5
    [HttpGet]
    public async Task<IActionResult> MarkNonCompliant(int id)
    {
        var bid = await GetBidAsync(id);
        if (bid == null)
        {
            return NotFound();
        }

        string? error = CheckCanDecide(bid);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Review), new { tenderId = bid.TenderId });
        }

        ViewBag.Bid = bid;
        return View(new NonCompliantViewModel());
    }

    // POST: /Compliance/MarkNonCompliant/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNonCompliant(int id, NonCompliantViewModel model)
    {
        var bid = await GetBidAsync(id);
        if (bid == null)
        {
            return NotFound();
        }

        // Check again on POST
        string? error = CheckCanDecide(bid);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Review), new { tenderId = bid.TenderId });
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Bid = bid;
            return View(model);
        }

        bid.Status = "Non-Compliant";
        bid.ComplianceNotes = model.Reason.Trim();
        await _context.SaveChangesAsync();

        // TODO: audit log + notification (team mechanism) - bid marked non-compliant

        TempData["Msg"] = $"The bid of {bid.SupplierProfile.CompanyName} is now Non-Compliant.";
        return RedirectToAction(nameof(Review), new { tenderId = bid.TenderId });
    }

    // POST: /Compliance/SendToEvaluation?tenderId=3
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToEvaluation(int tenderId)
    {
        var tender = await _context.Tenders.FindAsync(tenderId);
        if (tender == null)
        {
            return NotFound();
        }

        // Rule 1: the tender must be Closed
        if (tender.Status != "Closed")
        {
            TempData["Error"] = "Only a closed tender can be sent to evaluation.";
            return RedirectToAction(nameof(Review), new { tenderId = tender.Id });
        }

        // Rule 2: every submitted bid must be checked
        int submitted = await CountBidsAsync(tender.Id, "Submitted");
        if (submitted > 0)
        {
            TempData["Error"] = $"{submitted} bid(s) are not checked yet.";
            return RedirectToAction(nameof(Review), new { tenderId = tender.Id });
        }

        // Rule 3: at least one bid must be Compliant
        int compliant = await CountBidsAsync(tender.Id, "Compliant");
        if (compliant == 0)
        {
            TempData["Error"] = "At least one bid must be Compliant before sending the tender to evaluation.";
            return RedirectToAction(nameof(Review), new { tenderId = tender.Id });
        }

        tender.Status = "Under Evaluation";
        await _context.SaveChangesAsync();

        // TODO: audit log + notification (team mechanism) - tender sent to evaluation

        TempData["Msg"] = $"\"{tender.Title}\" was sent to evaluation with {compliant} compliant bid(s).";
        return RedirectToAction(nameof(Pending));
    }

    // ----------------- helper methods -----------------

    // A bid with its tender and supplier, or null
    private async Task<Bid?> GetBidAsync(int bidId)
    {
        return await _context.Bids
            .Include(b => b.Tender)
            .Include(b => b.SupplierProfile)
            .FirstOrDefaultAsync(b => b.Id == bidId);
    }

    // Returns an error message, or null if the officer can mark this bid now
    private string? CheckCanDecide(Bid bid)
    {
        if (bid.Tender.Status != "Closed")
        {
            return "Bids can be checked only while the tender is Closed.";
        }

        if (bid.Status != "Submitted")
        {
            return "Only a submitted bid can be checked. This bid is already " + bid.Status + ".";
        }

        return null;
    }

    // How many bids of a tender have this status
    private async Task<int> CountBidsAsync(int tenderId, string status)
    {
        return await _context.Bids.CountAsync(b => b.TenderId == tenderId && b.Status == status);
    }
}
