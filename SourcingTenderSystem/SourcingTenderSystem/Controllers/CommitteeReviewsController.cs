using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.Reviewers)]
public sealed class CommitteeReviewsController(ApplicationDbContext db, BidAiAdvisor advisor, ExchangeRates rates) : RequisitionBaseController(db)
{
    private IQueryable<TenderCommitteeMember> Memberships(int employeeId) =>
        Db.TenderCommitteeMembers.Where(m => m.EmployeeProfileId == employeeId && m.CommitteeType == "Evaluation");

    [HttpGet]
    public Task<IActionResult> Index() => ShowAssignments();
    [HttpGet]
    public Task<IActionResult> Assignments() => ShowAssignments();

    private async Task<IActionResult> ShowAssignments()
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        return View(await Memberships(employee.Id).AsNoTracking().OrderByDescending(m => m.TenderId)
            .Select(m => new CommitteeAssignment(m.TenderId, m.Tender.Title, m.Tender.Status, m.MemberRole)).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var tender = await Db.Tenders.Where(t => Memberships(employee.Id).Any(m => m.TenderId == t.Id)).AsNoTracking().AsSplitQuery()
            .Include(t => t.EvaluationCriteria)
            .Include(t => t.Bids.Where(b => b.Status == "Compliant")).ThenInclude(b => b.SupplierProfile)
            .SingleOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        var technical = await Db.TechnicalEvaluations.Where(e => e.Bid.TenderId == id && e.EvaluatorEmployeeProfileId == employee.Id)
            .Select(e => new { e.BidId, e.EvaluationCriteriaId }).ToListAsync();
        var financial = await Db.FinancialEvaluations.Where(e => e.Bid.TenderId == id && e.EvaluatorEmployeeProfileId == employee.Id)
            .Select(e => new { e.BidId, e.EvaluationCriteriaId }).ToListAsync();
        var progress = technical.Concat(financial).Distinct().GroupBy(e => e.BidId).ToDictionary(g => g.Key, g => g.Count());
        ViewBag.Submission = await Db.EvaluationSubmissions.AsNoTracking().SingleOrDefaultAsync(s => s.TenderId == id && s.EmployeeProfileId == employee.Id);
        return View(new CommitteeTender(tender, false, progress));
    }

    [HttpGet]
    public async Task<IActionResult> Bid(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        return await ShowBid(id, employee.Id);
    }

    private async Task<IActionResult> ShowBid(int id, int employeeId)
    {
        var bid = await Db.Bids.Where(b => b.Status == "Compliant" && b.SubmittedAt != null && Memberships(employeeId).Any(m => m.TenderId == b.TenderId)).AsNoTracking().AsSplitQuery().Include(b => b.SupplierProfile)
            .Include(b => b.Tender).ThenInclude(t => t.EvaluationCriteria)
            .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequisitionItem)
            .Include(b => b.BidDocuments)
            .Include(b => b.TechnicalEvaluations.Where(e => e.EvaluatorEmployeeProfileId == employeeId))
            .Include(b => b.FinancialEvaluations.Where(e => e.EvaluatorEmployeeProfileId == employeeId))
            .SingleOrDefaultAsync(b => b.Id == id);
        if (bid is null) return NotFound();
        var scores = bid.Tender.EvaluationCriteria.Where(c => c.Type is "Technical" or "Financial").OrderBy(c => c.Type)
            .ThenBy(c => c.Id).Select(c =>
            {
                var t = bid.TechnicalEvaluations.FirstOrDefault(e => e.EvaluationCriteriaId == c.Id);
                var f = bid.FinancialEvaluations.FirstOrDefault(e => e.EvaluationCriteriaId == c.Id);
                return new CommitteeScoreRow(c, t?.Score ?? f?.Score, t?.Notes ?? f?.Notes, f?.TaxesAndFees, f?.ExtraCosts);
            }).ToList();
        ViewBag.ReadOnly = bid.Tender.Status != "Under Evaluation" || await Db.EvaluationSubmissions.AnyAsync(s =>
            s.TenderId == bid.TenderId && s.EmployeeProfileId == employeeId);
        return View("Bid", new CommitteeBid(bid, scores));
    }

    [HttpPost]
    public async Task<IActionResult> SaveScore(int id, int criterionId, CommitteeScoreInput input)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var tenderId = await Db.Bids.Where(b => b.Id == id).Select(b => (int?)b.TenderId).SingleOrDefaultAsync();
        if (tenderId is null) return NotFound();
        await using var tx = await TenderRules.LockAsync(Db, tenderId.Value);
        if (await Db.EvaluationSubmissions.AnyAsync(s => s.TenderId == tenderId && s.EmployeeProfileId == employee.Id))
            return Conflict("Submitted evaluations are read-only.");
        var bid = await ReviewableBids(employee.Id).SingleOrDefaultAsync(b => b.Id == id);
        if (bid is null) return NotFound();
        var criterion = await Db.EvaluationCriteria.SingleOrDefaultAsync(c => c.Id == criterionId && c.TenderId == bid.TenderId);
        if (criterion is null || criterion.Type is not ("Technical" or "Financial")) return NotFound();
        if (await Db.EvaluationCriteria.Where(c => c.TenderId == bid.TenderId).SumAsync(c => c.WeightPercent) != 100)
            ModelState.AddModelError("", "Complete the criteria so their weights total 100% before scoring.");
        if (input.Score is decimal score && score != EvaluationRules.Round(score) ||
            input.Taxes != EvaluationRules.Round(input.Taxes) || input.Extras != EvaluationRules.Round(input.Extras))
            ModelState.AddModelError("", "Use at most two decimal places.");
        if (input.Score < 0 || input.Score > criterion.MaxScore)
            ModelState.AddModelError(nameof(input.Score), $"Enter a score between 0 and {criterion.MaxScore}.");
        if (!ModelState.IsValid)
        {
            TempData["ReviewError"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Enter a valid number." : e.ErrorMessage));
            return RedirectToAction(nameof(Bid), new { id });
        }

        if (criterion.Type == "Technical")
        {
            var row = await Db.TechnicalEvaluations.FirstOrDefaultAsync(e => e.BidId == id &&
                e.EvaluationCriteriaId == criterionId && e.EvaluatorEmployeeProfileId == employee.Id);
            if (row is null)
            {
                row = new TechnicalEvaluation { BidId = id, EvaluationCriteriaId = criterionId,
                    EvaluatorEmployeeProfileId = employee.Id, CreatedAt = DateTime.UtcNow };
                Db.TechnicalEvaluations.Add(row);
            }
            row.Score = input.Score!.Value;
            row.Notes = input.Notes?.Trim();
        }
        else
        {
            var row = await Db.FinancialEvaluations.FirstOrDefaultAsync(e => e.BidId == id &&
                e.EvaluationCriteriaId == criterionId && e.EvaluatorEmployeeProfileId == employee.Id);
            if (row is null)
            {
                row = new FinancialEvaluation { BidId = id, EvaluationCriteriaId = criterionId, EvaluatorEmployeeProfileId = employee.Id };
                Db.FinancialEvaluations.Add(row);
            }
            row.Score = input.Score!.Value;
            row.Notes = input.Notes?.Trim();
            row.TaxesAndFees = input.Taxes;
            row.ExtraCosts = input.Extras;
            row.TotalCost = bid.TotalPrice + input.Taxes + input.Extras;
        }
        await Db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = "Your review score was saved.";
        return RedirectToAction(nameof(Bid), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SubmitEvaluation(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await TenderRules.LockAsync(Db, id);
        if (!await Reviewable(employee.Id).AnyAsync(t => t.Id == id)) return NotFound();
        if (await Db.EvaluationSubmissions.AnyAsync(s => s.TenderId == id && s.EmployeeProfileId == employee.Id))
            return Conflict("Evaluation already submitted.");
        var criteria = await Db.EvaluationCriteria.Where(c => c.TenderId == id).ToListAsync();
        var bids = await Db.Bids.Where(b => b.TenderId == id && b.Status == "Compliant" && b.SubmittedAt != null).Select(b => b.Id).ToListAsync();
        var scores = (await EvaluationRules.Scores(Db, id)).Where(s => s.EmployeeId == employee.Id);
        if (!EvaluationRules.CriteriaValid(criteria) || bids.Count == 0 || await EvaluationRules.Unresolved(Db, id).AnyAsync() ||
            EvaluationRules.CompleteCells(bids, criteria, scores) != bids.Count * criteria.Count)
        {
            TempData["ReviewError"] = "Save valid scores for every criterion and every compliant bid before submitting.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var now = DateTime.UtcNow;
        Db.EvaluationSubmissions.Add(new EvaluationSubmission { TenderId = id, EmployeeProfileId = employee.Id, SubmittedAt = now });
        EvaluationRules.Audit(Db, id, employee.Id, "EvaluationSubmitted", $"Submitted scores for {bids.Count} bids.", now);
        await Db.SaveChangesAsync();
        await tx.CommitAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    // ------------------------------------------------------------ AI assistant (advisory only)

    /// <summary>The recommendation box on the tender page. Loaded in the background because the model can be slow.</summary>
    [HttpGet]
    public async Task<IActionResult> AiRecommendation(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var snapshot = await AiSnapshotAsync(id, employee.Id);
        if (snapshot is null) return NotFound();
        try
        {
            return PartialView("_AiRecommendation", new AiRecommendationView(id, await advisor.GetOrCreateAsync(snapshot), null));
        }
        catch (AiAdvisorUnavailableException ex)
        {
            return PartialView("_AiRecommendation", new AiRecommendationView(id, null, ex.Message));
        }
    }

    /// <summary>The full AI report as a standalone HTML file (opens in any browser and prints to PDF).</summary>
    [HttpGet]
    public async Task<IActionResult> AiReport(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var snapshot = await AiSnapshotAsync(id, employee.Id);
        if (snapshot is null) return NotFound();
        AiBidAnalysis analysis;
        try { analysis = await advisor.GetOrCreateAsync(snapshot); }
        catch (AiAdvisorUnavailableException ex)
        {
            TempData["ReviewError"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
        Response.Headers.ContentDisposition = $"attachment; filename=\"ai-bid-analysis-tender-{id}.html\"";
        return View("AiReport", analysis);
    }

    private async Task<AiTenderSnapshot?> AiSnapshotAsync(int tenderId, int employeeId)
    {
        var tender = await Reviewable(employeeId).AsNoTracking().AsSplitQuery()
            .Include(t => t.PurchaseRequisition).ThenInclude(r => r.Items)
            .Include(t => t.EvaluationCriteria)
            .Include(t => t.Bids.Where(b => b.Status == "Compliant")).ThenInclude(b => b.SupplierProfile)
            .Include(t => t.Bids.Where(b => b.Status == "Compliant")).ThenInclude(b => b.BidItems).ThenInclude(i => i.PurchaseRequisitionItem)
            .Include(t => t.Bids.Where(b => b.Status == "Compliant")).ThenInclude(b => b.BidDocuments)
            .SingleOrDefaultAsync(t => t.Id == tenderId);
        if (tender is null) return null;
        var usd = new Dictionary<int, decimal?>();
        if (tender.Bids.Select(b => b.Currency).Distinct().Count() > 1)
        {
            var rateDate = ExchangeRates.ReferenceDate(tender.CloseDate, DateTime.UtcNow);
            foreach (var b in tender.Bids) usd[b.Id] = (await rates.ConvertAsync(b.TotalPrice, b.Currency, rateDate))?.UsdAmount;
        }
        return new AiTenderSnapshot(tender.Id, tender.Title, tender.Description, tender.Terms,
            tender.PurchaseRequisition.Items.OrderBy(i => i.Id)
                .Select(i => new AiRequestedItem(i.Id, i.ItemName, i.Quantity, i.Unit, i.Specifications)).ToList(),
            tender.EvaluationCriteria.OrderBy(c => c.Type).ThenBy(c => c.Id)
                .Select(c => new AiCriterion(c.Name, c.Type, c.WeightPercent, c.MaxScore)).ToList(),
            tender.Bids.OrderBy(b => b.Id).Select(b => new AiBidSnapshot(b.Id, b.SupplierProfile.CompanyName, b.TotalPrice,
                b.Currency, b.DeliveryDays, b.ValidityDays,
                b.BidItems.OrderBy(i => i.Id).Select(i => new AiBidLine(i.PurchaseRequisitionItemId,
                    i.PurchaseRequisitionItem.ItemName, i.Quantity, i.UnitPrice, i.Notes)).ToList(),
                b.BidDocuments.OrderBy(d => d.Id).Select(d => $"{d.DocumentType}: {d.FileName}").ToList(),
                usd.GetValueOrDefault(b.Id))).ToList());
    }

    private IQueryable<Tender> Reviewable(int employeeId) => Db.Tenders.Where(t => t.Status == "Under Evaluation" &&
        Memberships(employeeId).Any(m => m.TenderId == t.Id));
    private IQueryable<Bid> ReviewableBids(int employeeId) => Db.Bids.Where(b => b.Status == "Compliant" && b.SubmittedAt != null &&
        Reviewable(employeeId).Any(t => t.Id == b.TenderId));

}
