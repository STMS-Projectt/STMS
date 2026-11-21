using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.Reviewers)]
public sealed class CommitteeReviewsController(ApplicationDbContext db, BidAiAdvisor advisor) : RequisitionBaseController(db)
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
        var weights = await EvaluationRules.ReviewerWeights(Db, id, employee.Id, tender.EvaluationCriteria.ToArray());
        var progress = technical.Concat(financial).Distinct().Where(e => weights.ContainsKey(e.EvaluationCriteriaId))
            .GroupBy(e => e.BidId).ToDictionary(g => g.Key, g => g.Count());
        var submission = await Db.EvaluationSubmissions.AsNoTracking().SingleOrDefaultAsync(s => s.TenderId == id && s.EmployeeProfileId == employee.Id);
        ViewBag.Submission = submission;
        return View(new CommitteeTender(tender, tender.Status == "Under Evaluation" && submission is null, progress, weights));
    }

    [HttpGet]
    public async Task<IActionResult> Weights(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var tender = await EditableWeights(employee.Id).AsNoTracking().Include(t => t.EvaluationCriteria).SingleOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        var criteria = tender.EvaluationCriteria.OrderBy(c => c.Type).ThenBy(c => c.Id).ToList();
        var weights = await EvaluationRules.ReviewerWeights(Db, id, employee.Id, criteria);
        return View(new CommitteeWeights(id, tender.Title, criteria, weights));
    }

    [HttpPost]
    public async Task<IActionResult> SaveWeights(int id, List<EvaluationWeightInput> weights)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await TenderRules.LockAsync(Db, id);
        var tender = await EditableWeights(employee.Id).AsNoTracking().Include(t => t.EvaluationCriteria).SingleOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        var allowed = tender.EvaluationCriteria.Select(c => c.Id).ToHashSet();
        var selected = weights.Where(w => w.Selected).ToList();
        if (!ModelState.IsValid || !EvaluationRules.CriteriaValid(tender.EvaluationCriteria.ToArray()) ||
            weights.Select(w => w.CriterionId).Distinct().Count() != weights.Count ||
            weights.Any(w => !allowed.Contains(w.CriterionId)) || selected.Any(w => w.WeightPercent is null) ||
            !EvaluationRules.ValidWeights(selected.ToDictionary(w => w.CriterionId, w => w.WeightPercent!.Value), allowed))
        {
            ViewBag.WeightError = "Choose at least one published criterion. Selected weights must be positive, have at most two decimals, and total 100%.";
            var attempted = selected.Where(w => allowed.Contains(w.CriterionId)).GroupBy(w => w.CriterionId)
                .ToDictionary(g => g.Key, g => g.First().WeightPercent ?? 0);
            return View(nameof(Weights), new CommitteeWeights(id, tender.Title,
                tender.EvaluationCriteria.OrderBy(c => c.Type).ThenBy(c => c.Id).ToList(), attempted));
        }

        var plan = await Db.EvaluationWeightPlans.FindAsync(id, employee.Id);
        if (plan is null)
        {
            plan = new EvaluationWeightPlan { TenderId = id, EmployeeProfileId = employee.Id };
            Db.EvaluationWeightPlans.Add(plan);
        }
        plan.CriteriaWeights = JsonSerializer.Serialize(selected.OrderBy(w => w.CriterionId).ToDictionary(w => w.CriterionId, w => w.WeightPercent!.Value));
        plan.UpdatedAt = DateTime.UtcNow;
        EvaluationRules.Audit(Db, id, employee.Id, "EvaluationWeightsSaved", plan.CriteriaWeights, plan.UpdatedAt);
        await Db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = "Your criteria weights were saved.";
        return RedirectToAction(nameof(Weights), new { id });
    }

    [HttpPost, EnableRateLimiting("evaluation-weight-advice")]
    public async Task<IActionResult> SuggestWeights(int id, [FromForm] List<int> criterionIds,
        [FromServices] EvaluationWeightAdvisor weightsAdvisor, CancellationToken cancellationToken)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var tender = await EditableWeights(employee.Id).AsNoTracking().Include(t => t.EvaluationCriteria).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tender is null) return NotFound();
        var selected = tender.EvaluationCriteria.Where(c => criterionIds.Contains(c.Id)).OrderBy(c => c.Id).ToList();
        if (!ModelState.IsValid || !EvaluationRules.CriteriaValid(tender.EvaluationCriteria.ToArray()) ||
            criterionIds.Count is < 1 or > 40 || selected.Count != criterionIds.Count)
            return BadRequest(new { message = "Choose 1 to 40 published criteria for an AI suggestion." });
        try { return Ok(await weightsAdvisor.SuggestAsync(tender.Title, tender.Description, selected, cancellationToken)); }
        catch (AiAdvisorUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
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
        var weights = await EvaluationRules.ReviewerWeights(Db, bid.TenderId, employeeId, bid.Tender.EvaluationCriteria.ToArray());
        var scores = bid.Tender.EvaluationCriteria.Where(c => weights.ContainsKey(c.Id)).OrderBy(c => c.Type)
            .ThenBy(c => c.Id).Select(c =>
            {
                var t = bid.TechnicalEvaluations.FirstOrDefault(e => e.EvaluationCriteriaId == c.Id);
                var f = bid.FinancialEvaluations.FirstOrDefault(e => e.EvaluationCriteriaId == c.Id);
                return new CommitteeScoreRow(c, t?.Score ?? f?.Score, t?.Notes ?? f?.Notes, f?.TaxesAndFees, f?.ExtraCosts, weights[c.Id]);
            }).ToList();
        ViewBag.ReadOnly = bid.Tender.Status != "Under Evaluation" || await Db.EvaluationSubmissions.AnyAsync(s =>
            s.TenderId == bid.TenderId && s.EmployeeProfileId == employeeId);
        return View("Bid", new CommitteeBid(bid, scores));
    }

    [HttpPost]
    public async Task<IActionResult> SaveChecklist(int id, List<CommitteeCheckInput> checks)
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
        var published = await Db.EvaluationCriteria.AsNoTracking().Where(c => c.TenderId == bid.TenderId).ToListAsync();
        var weights = await EvaluationRules.ReviewerWeights(Db, bid.TenderId, employee.Id, published);
        if (weights.Count == 0 || checks.Count != weights.Count ||
            checks.Select(c => c.CriterionId).Distinct().Count() != checks.Count ||
            checks.Any(c => !weights.ContainsKey(c.CriterionId)))
            return Conflict("The checklist does not match your active evaluation criteria. Reload the bid and try again.");
        if (!ModelState.IsValid || checks.Any(c => c.Taxes != EvaluationRules.Round(c.Taxes) || c.Extras != EvaluationRules.Round(c.Extras)))
        {
            TempData["ReviewError"] = "Enter valid non-negative costs with up to two decimals and notes of up to 1,000 characters.";
            return RedirectToAction(nameof(Bid), new { id });
        }
        var byId = published.ToDictionary(c => c.Id);
        var technical = await Db.TechnicalEvaluations.Where(e => e.BidId == id && e.EvaluatorEmployeeProfileId == employee.Id)
            .ToDictionaryAsync(e => e.EvaluationCriteriaId);
        var financial = await Db.FinancialEvaluations.Where(e => e.BidId == id && e.EvaluatorEmployeeProfileId == employee.Id)
            .ToDictionaryAsync(e => e.EvaluationCriteriaId);
        foreach (var check in checks)
        {
            var criterion = byId[check.CriterionId];
            var score = check.Met ? criterion.MaxScore : 0;
            if (criterion.Type == "Technical")
            {
                if (!technical.TryGetValue(criterion.Id, out var row))
                {
                    row = new TechnicalEvaluation { BidId = id, EvaluationCriteriaId = criterion.Id,
                        EvaluatorEmployeeProfileId = employee.Id, CreatedAt = DateTime.UtcNow };
                    Db.TechnicalEvaluations.Add(row);
                }
                row.Score = score;
                row.Notes = check.Notes?.Trim();
            }
            else
            {
                if (!financial.TryGetValue(criterion.Id, out var row))
                {
                    row = new FinancialEvaluation { BidId = id, EvaluationCriteriaId = criterion.Id, EvaluatorEmployeeProfileId = employee.Id };
                    Db.FinancialEvaluations.Add(row);
                }
                row.Score = score;
                row.Notes = check.Notes?.Trim();
                row.TaxesAndFees = check.Taxes;
                row.ExtraCosts = check.Extras;
                row.TotalCost = bid.TotalPrice + check.Taxes + check.Extras;
            }
        }
        await Db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = "Your evaluation checklist was saved.";
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
        var weights = await EvaluationRules.ReviewerWeights(Db, id, employee.Id, criteria);
        var selected = criteria.Where(c => weights.ContainsKey(c.Id)).ToList();
        var bids = await Db.Bids.Where(b => b.TenderId == id && b.Status == "Compliant" && b.SubmittedAt != null).Select(b => b.Id).ToListAsync();
        var scores = (await EvaluationRules.Scores(Db, id)).Where(s => s.EmployeeId == employee.Id);
        if (selected.Count == 0 || bids.Count == 0 || await EvaluationRules.Unresolved(Db, id).AnyAsync() ||
            EvaluationRules.CompleteCells(bids, selected, scores) != bids.Count * selected.Count)
        {
            TempData["ReviewError"] = "Save valid scores for each selected criterion and every compliant bid before submitting.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var now = DateTime.UtcNow;
        Db.EvaluationSubmissions.Add(new EvaluationSubmission { TenderId = id, EmployeeProfileId = employee.Id, SubmittedAt = now });
        EvaluationRules.Audit(Db, id, employee.Id, "EvaluationSubmitted",
            $"Submitted scores for {bids.Count} bids. Weights: {JsonSerializer.Serialize(weights)}", now);
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
    public async Task<IActionResult> AiReport(int id, [FromServices] ProtectedPdf pdf, [FromServices] ILogger<CommitteeReviewsController> logger)
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
        try
        {
            using var document = AiBidReportPdf.Build(analysis);
            pdf.AddWatermark(document, User, DateTime.UtcNow);
            var bytes = await pdf.Render(document, null);
            Response.Headers.CacheControl = "private, no-store";
            return File(bytes, "application/pdf", $"ai-bid-analysis-tender-{id}.pdf");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not build the AI bid analysis PDF for tender {TenderId}", id);
            TempData["ReviewError"] = "The AI report PDF could not be created. Please try again.";
            return RedirectToAction(nameof(Details), new { id });
        }
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
        return new AiTenderSnapshot(tender.Id, tender.Title, tender.Description, tender.Terms,
            tender.PurchaseRequisition.Items.OrderBy(i => i.Id)
                .Select(i => new AiRequestedItem(i.Id, i.ItemName, i.Quantity, i.Unit, i.Specifications)).ToList(),
            tender.EvaluationCriteria.OrderBy(c => c.Type).ThenBy(c => c.Id)
                .Select(c => new AiCriterion(c.Name, c.Type, c.WeightPercent, c.MaxScore)).ToList(),
            tender.Bids.OrderBy(b => b.Id).Select(b => new AiBidSnapshot(b.Id, b.SupplierProfile.CompanyName, b.TotalPrice,
                b.Currency, b.DeliveryDays, b.ValidityDays,
                b.BidItems.OrderBy(i => i.Id).Select(i => new AiBidLine(i.PurchaseRequisitionItemId,
                    i.PurchaseRequisitionItem.ItemName, i.Quantity, i.UnitPrice, i.Notes)).ToList(),
                b.BidDocuments.OrderBy(d => d.Id).Select(d => $"{d.DocumentType}: {d.FileName}").ToList())).ToList());
    }

    private IQueryable<Tender> Reviewable(int employeeId) => Db.Tenders.Where(t => t.Status == "Under Evaluation" &&
        Memberships(employeeId).Any(m => m.TenderId == t.Id));
    private IQueryable<Tender> EditableWeights(int employeeId) => Reviewable(employeeId)
        .Where(t => !Db.EvaluationSubmissions.Any(s => s.TenderId == t.Id && s.EmployeeProfileId == employeeId));
    private IQueryable<Bid> ReviewableBids(int employeeId) => Db.Bids.Where(b => b.Status == "Compliant" && b.SubmittedAt != null &&
        Reviewable(employeeId).Any(t => t.Id == b.TenderId));

}
