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

[Authorize(Roles = Roles.Admin + "," + Roles.SourcingOfficer + "," + Roles.SourcingManager + "," + Roles.TopCommittee)]
public sealed class EvaluationWorkflowController(ApplicationDbContext db, SupplierFileStore files) : RequisitionBaseController(db)
{
    private const string Officer = "Pending Officer Approval";
    private const string Manager = "Pending Manager Approval";
    private const string Top = "Pending Top Committee";
    private const string Approved = "Committee Approved";

    private IQueryable<Tender> Visible(int employeeId)
    {
        var query = Db.Tenders.Where(t => t.Status == "Under Evaluation" || t.EvaluationReport != null);
        if (User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SourcingOfficer)) return query;
        if (User.IsInRole(Roles.TopCommittee))
            return query.Where(t => t.EvaluationReport != null &&
                (t.EvaluationReport.Status == Top || t.EvaluationReport.Status == Approved));
        return query.Where(t => t.CommitteeMembers.Any(m => m.EmployeeProfileId == employeeId &&
            m.CommitteeType == "Evaluation" && m.MemberRole == "Sourcing Manager"));
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        return View(await Visible(employee.Id).AsNoTracking().Include(t => t.EvaluationReport)
            .OrderByDescending(t => t.Id).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var tender = await Load(id, employee.Id);
        if (tender is null) return NotFound();
        var scores = await ReadScores(tender);
        var progress = Progress(tender, scores);
        return View(new EvaluationWorkflowDetails(tender, progress, CanUpload(tender, employee.Id, progress),
            NextStage(tender, employee.Id), scores));
    }

    [HttpPost, Authorize(Roles = Roles.SourcingManager), RequestSizeLimit(SupplierFileStore.MaxBytes + 65536)]
    public async Task<IActionResult> Upload(int id, FinalReportInput input, CancellationToken cancellationToken)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var tender = await Load(id, employee.Id);
        if (tender is null) return NotFound();
        if (!CanUpload(tender, employee.Id, Progress(tender, await ReadScores(tender)))) return Conflict("All four members must finish first.");
        if (!ModelState.IsValid || !tender.Bids.Any(b => b.Id == input.RecommendedBidId))
        {
            TempData["WorkflowError"] = "Select a compliant bid and provide a summary and PDF report.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var key = await files.SaveAsync(input.File!, cancellationToken);
        if (key is null)
        {
            TempData["WorkflowError"] = "Choose a PDF report up to 10 MB.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            Db.EvaluationReports.Add(new EvaluationReport
            {
                TenderId = id, RecommendedBidId = input.RecommendedBidId, Summary = input.Summary.Trim(),
                SupplierComparison = "", Recommendation = "See the attached evaluation report.",
                FileKey = key, Status = Officer, PreparedByEmployeeProfileId = employee.Id, CreatedAt = DateTime.UtcNow
            });
            tender.Status = "Evaluation Closed";
            await Db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            files.Delete(key);
            throw;
        }
        TempData["Message"] = "Evaluation closed. The report is with the sourcing officer.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var key = await Visible(employee.Id).Where(t => t.Id == id)
            .Select(t => t.EvaluationReport == null ? null : t.EvaluationReport.FileKey).SingleOrDefaultAsync();
        return key is not null && files.Open(key) is { } stream
            ? File(stream, "application/pdf", $"evaluation-report-{id}.pdf") : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> Approve(int id, string? comments, string expectedStage)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var tender = await Load(id, employee.Id);
        if (tender?.EvaluationReport is not { } report) return NotFound();
        if (report.Status != expectedStage) return Conflict("The report has already moved to another stage.");
        var next = NextStage(tender, employee.Id);
        if (next is null) return Forbid();
        if (comments?.Length > 1000)
        {
            TempData["WorkflowError"] = "Keep approval notes to 1,000 characters or fewer.";
            return RedirectToAction(nameof(Details), new { id });
        }

        Db.TenderApprovals.Add(new TenderApproval { EvaluationReportId = report.Id,
            ApproverEmployeeProfileId = employee.Id, Decision = next, Comments = comments?.Trim(),
            SelectedBidId = report.RecommendedBidId, DecidedAt = DateTime.UtcNow });
        report.Status = next;
        if (next == Approved) tender.Status = "Committee Approved";
        await Db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = next == Approved ? "The top committee approved the report." : "Report approved and forwarded.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private Task<Tender?> Load(int id, int employeeId) => Visible(employeeId).AsSplitQuery()
        .Include(t => t.CommitteeMembers.Where(m => m.CommitteeType == "Evaluation")).ThenInclude(m => m.EmployeeProfile)
        .Include(t => t.EvaluationCriteria)
        .Include(t => t.Bids.Where(b => b.Status == "Compliant")).ThenInclude(b => b.SupplierProfile)
        .Include(t => t.EvaluationReport).ThenInclude(r => r!.TenderApprovals).ThenInclude(a => a.ApproverEmployeeProfile)
        .SingleOrDefaultAsync(t => t.Id == id);

    private async Task<List<EvaluationScore>> ReadScores(Tender tender)
    {
        var technical = await Db.TechnicalEvaluations.Where(e => e.Bid.TenderId == tender.Id && e.Bid.Status == "Compliant" &&
            e.EvaluationCriterion.TenderId == tender.Id && e.EvaluationCriterion.Type == "Technical")
            .Select(e => new EvaluationScore(e.EvaluatorEmployeeProfileId, e.BidId, e.EvaluationCriteriaId, e.Score, e.Notes)).ToListAsync();
        var financial = await Db.FinancialEvaluations.Where(e => e.Bid.TenderId == tender.Id && e.Bid.Status == "Compliant" &&
            e.EvaluationCriterion.TenderId == tender.Id && e.EvaluationCriterion.Type == "Financial")
            .Select(e => new EvaluationScore(e.EvaluatorEmployeeProfileId, e.BidId, e.EvaluationCriteriaId, e.Score, e.Notes)).ToListAsync();
        return technical.Concat(financial).ToList();
    }

    private static List<EvaluationProgress> Progress(Tender tender, List<EvaluationScore> scores)
    {
        var required = tender.Bids.Count * tender.EvaluationCriteria.Count(c => c.Type is "Technical" or "Financial");
        return tender.CommitteeMembers.Select(m => new EvaluationProgress(m.EmployeeProfile.FullName, m.MemberRole,
            scores.Where(s => s.EmployeeId == m.EmployeeProfileId).Select(s => (s.BidId, s.CriterionId)).Distinct().Count(),
            required)).ToList();
    }

    private bool CanUpload(Tender tender, int employeeId, IReadOnlyList<EvaluationProgress> progress) =>
        User.IsInRole(Roles.SourcingManager) && IsAssignedManager(tender, employeeId) &&
        tender.Status == "Under Evaluation" && tender.EvaluationReport == null &&
        tender.CommitteeMembers.Select(m => m.EmployeeProfileId).Distinct().Count() == 4 &&
        progress.Count == 4 && progress.All(p => p.Complete);

    private static bool IsAssignedManager(Tender tender, int employeeId) =>
        tender.CommitteeMembers.Any(m => m.EmployeeProfileId == employeeId && m.MemberRole == "Sourcing Manager");

    private string? NextStage(Tender tender, int employeeId) => tender.EvaluationReport?.Status switch
    {
        Officer when User.IsInRole(Roles.SourcingOfficer) => Manager,
        Manager when User.IsInRole(Roles.SourcingManager) && IsAssignedManager(tender, employeeId) => Top,
        Top when User.IsInRole(Roles.TopCommittee) => Approved,
        _ => null
    };
}
