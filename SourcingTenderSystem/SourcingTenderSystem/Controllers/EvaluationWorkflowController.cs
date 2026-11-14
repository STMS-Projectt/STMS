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
    private const string Approved = "Awarded";
    private const string Cancelled = "Cancelled";
    private const string Vote = "Top Committee Approved";

    private IQueryable<Tender> Visible(int employeeId)
    {
        var query = Db.Tenders.Where(t => t.Status == "Under Evaluation" || t.EvaluationReport != null);
        if (User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SourcingOfficer)) return query;
        if (User.IsInRole(Roles.TopCommittee))
            return query.Where(t => t.EvaluationReport != null &&
                (t.EvaluationReport.Status == Top || t.EvaluationReport.Status == Approved || t.EvaluationReport.Status == Cancelled || t.EvaluationReport.Status == TenderRules.AwaitingReturn || t.EvaluationReport.Status == TenderRules.ReadyToRetender) &&
                t.CommitteeMembers.Any(m => m.CommitteeType == "Top" && m.EmployeeProfileId == employeeId));
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
        ViewData["CanRetender"] = tender.EvaluationReport?.Status == TenderRules.ReadyToRetender &&
            !await Db.Tenders.AnyAsync(t => t.PurchaseRequisitionId == tender.PurchaseRequisitionId &&
                (t.Id > tender.Id || t.Status != Cancelled));
        var scores = await EvaluationRules.Scores(Db, id);
        var progress = await Progress(tender, scores);
        ViewBag.Comparison = tender.EvaluationReport?.SupplierComparison is { Length: > 0 } comparison &&
            tender.EvaluationReport.ResultsSnapshot != null
            ? System.Text.Json.JsonSerializer.Deserialize<List<SupplierEvaluationResult>>(comparison)
            : progress.Count == 4 && progress.All(p => p.Complete) ? await Compare(tender, scores) : null;
        return View(new EvaluationWorkflowDetails(tender, progress, CanUpload(tender, employee.Id, progress),
            NextStage(tender, employee.Id), scores));
    }

    [HttpPost, Authorize(Roles = Roles.SourcingManager), RequestSizeLimit(SupplierFileStore.MaxBytes + 65536)]
    public async Task<IActionResult> Upload(int id, FinalReportInput input, CancellationToken cancellationToken)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await TenderRules.LockAsync(Db, id, cancellationToken);
        var tender = await Load(id, employee.Id);
        if (tender is null) return NotFound();
        var scores = await EvaluationRules.Scores(Db, id);
        var progress = await Progress(tender, scores);
        if (!CanUpload(tender, employee.Id, progress) || await EvaluationRules.Unresolved(Db, id).AnyAsync()) return Conflict("All four members must finish first.");
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(input.Summary) || !tender.Bids.Any(b => b.Id == input.RecommendedBidId))
        {
            TempData["WorkflowError"] = "Select a compliant bid and provide a summary and PDF report.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var comparison = await Compare(tender, scores);
        var snapshot = System.Text.Json.JsonSerializer.Serialize(new {
            tender.Id, tender.Title, tender.Description, tender.Terms, tender.OpenDate, tender.CloseDate,
            Criteria = tender.EvaluationCriteria.Select(c => new { c.Id, c.Name, c.Type, c.MaxScore, c.WeightPercent }),
            Scores = scores, Submissions = progress, Suppliers = comparison, input.RecommendedBidId,
            Justification = input.Summary.Trim()
        });
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
                SupplierComparison = System.Text.Json.JsonSerializer.Serialize(comparison), Recommendation = input.Summary.Trim(),
                ResultsSnapshot = snapshot,
                FileKey = key, Status = Officer, PreparedByEmployeeProfileId = employee.Id, CreatedAt = DateTime.UtcNow
            });
            EvaluationRules.Audit(Db, id, employee.Id, "EvaluationReportPrepared", $"Recommended bid #{input.RecommendedBidId}", DateTime.UtcNow);
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
    public Task<IActionResult> Download(int id, [FromServices] ProtectedPdf pdf, bool preview = false) =>
        ReportCopy(id, pdf, preview);

    [HttpPost]
    public Task<IActionResult> SignDocument(int id, string? password, [FromServices] ProtectedPdf pdf)
    {
        if (!User.IsInRole(Roles.SourcingManager) && !User.IsInRole(Roles.SourcingOfficer)) return Task.FromResult<IActionResult>(Forbid());
        return ReportCopy(id, pdf, false, true, password);
    }

    private async Task<IActionResult> ReportCopy(int id, ProtectedPdf pdf, bool preview, bool sign = false, string? password = null)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var key = await Visible(employee.Id).Where(t => t.Id == id)
            .Select(t => t.EvaluationReport == null ? null : t.EvaluationReport.FileKey).SingleOrDefaultAsync();
        return key is not null && files.Open(key) is { } stream
            ? await pdf.Serve(this, stream, $"evaluation-report-{id}.pdf", preview, sign, password) : NotFound();
    }

    [HttpGet]
    public async Task<IActionResult> DecisionPdf(int id, [FromServices] ProtectedPdf pdf)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var decision = await Db.TenderApprovals.AsNoTracking().Include(a => a.ApproverEmployeeProfile)
            .Include(a => a.EvaluationReport).ThenInclude(r => r.Tender)
            .Include(a => a.SelectedBid).ThenInclude(b => b!.SupplierProfile)
            .Where(a => Visible(employee.Id).Any(t => t.Id == a.EvaluationReport.TenderId))
            .SingleOrDefaultAsync(a => a.Id == id);
        if (decision is null) return NotFound();
        if (!pdf.SigningEnabled) return StatusCode(503, "The organization signing certificate has not been configured.");
        var lines = new[] {
            ("Tender", $"#{decision.EvaluationReport.TenderId} | {decision.EvaluationReport.Tender.Title}"),
            ("Decision", decision.Decision),
            ("Approved by", decision.ApproverEmployeeProfile.FullName),
            ("Recorded at", $"{decision.DecidedAt:yyyy-MM-dd HH:mm:ss} UTC"),
            ("Selected supplier", decision.SelectedBid?.SupplierProfile.CompanyName ?? "-"),
            ("Approval notes", decision.Comments ?? "-")
        };
        return await pdf.Decision(this, id, lines);
    }

    [HttpGet, Authorize(Roles = Roles.TopCommittee)]
    public async Task<IActionResult> Reject(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var tender = await Load(id, employee.Id);
        if (tender?.EvaluationReport?.Status != Top || NextStage(tender, employee.Id) == null) return NotFound();
        return View(tender);
    }

    [HttpPost]
    public async Task<IActionResult> Approve(int id, string? comments, string expectedStage, string? vote, string? password, bool confirmDecision, [FromServices] ProtectedPdf pdf)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        if (!confirmDecision || !await pdf.Confirm(User, password))
        {
            TempData["WorkflowError"] = "Confirm your decision and enter your current password.";
            return RedirectToAction(nameof(Details), new { id });
        }
        await using var tx = await TenderRules.LockAsync(Db, id);
        var tender = await Load(id, employee.Id);
        if (tender?.EvaluationReport is not { } report) return NotFound();
        if (report.Status != expectedStage) return Conflict("The report has already moved to another stage.");
        if (report.Status == Officer && tender.Status != "Under Evaluation" ||
            report.Status == Manager && tender.Status != "Awaiting Manager Review" ||
            report.Status == Top && tender.Status != "Awaiting Committee Decision")
            return Conflict("Tender and report are not at the expected stage.");
        if (report.ResultsSnapshot == null || !EvaluationRules.CriteriaValid(tender.EvaluationCriteria.ToArray()) || await EvaluationRules.Unresolved(Db, id).AnyAsync() ||
            !(await Progress(tender, await EvaluationRules.Scores(Db, id))).All(p => p.Complete) ||
            tender.CommitteeMembers.Count(m => m.CommitteeType == "Evaluation") != 4)
            return Conflict("An assembled report and four submitted evaluations are required.");
        var next = NextStage(tender, employee.Id);
        if (next is null) return Forbid();
        if (comments?.Length > 1000)
        {
            TempData["WorkflowError"] = "Keep approval notes to 1,000 characters or fewer.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!await Db.Bids.AnyAsync(b => b.TenderId == id && b.Id == report.RecommendedBidId &&
            b.SubmittedAt != null && b.Status == "Compliant"))
            return Conflict("The recommended bid is no longer eligible.");
        var decision = next;
        if (report.Status == Top)
        {
            if (vote is not ("approve" or "reject")) return BadRequest("Select approve or reject.");
            if (vote == "reject" && string.IsNullOrWhiteSpace(comments)) return BadRequest("A rejection reason is required.");
            var roster = tender.CommitteeMembers.Where(m => m.CommitteeType == "Top").Select(m => m.EmployeeProfileId).Distinct().ToList();
            if (roster.Count != 4 || !roster.Contains(employee.Id)) return Forbid();
            if (report.TenderApprovals.Any(a => a.ApproverEmployeeProfileId == employee.Id && (a.Decision == Vote || a.Decision == "Top Committee Rejected")))
                return Conflict("You already voted on this recommendation.");
            decision = vote == "reject" ? "Top Committee Rejected" : Vote;
            var previous = report.TenderApprovals.Where(a => a.Decision == Vote && roster.Contains(a.ApproverEmployeeProfileId))
                .Select(a => a.ApproverEmployeeProfileId).Distinct().Count();
            next = vote == "reject" ? Cancelled : previous == 3 ? Approved : Top;
        }
        else if (next == Top)
        {
            var roster = await TopVoters();
            if (roster.Count != 4)
            {
                TempData["WorkflowError"] = "Set up exactly four active TopCommittee accounts before forwarding.";
                return RedirectToAction(nameof(Details), new { id });
            }
            foreach (var member in roster)
                tender.CommitteeMembers.Add(new TenderCommitteeMember { EmployeeProfileId = member,
                    CommitteeType = "Top", MemberRole = "Top Committee Member" });
        }
        Db.TenderApprovals.Add(new TenderApproval { EvaluationReportId = report.Id,
            ApproverEmployeeProfileId = employee.Id, Decision = decision, Comments = comments?.Trim(),
            SelectedBidId = next == Cancelled ? null : report.RecommendedBidId, DecidedAt = DateTime.UtcNow });
        var now = DateTime.UtcNow;
        EvaluationRules.Audit(Db, id, employee.Id, decision, $"Evaluation report #{report.Id}", now);
        report.Status = next == Cancelled ? TenderRules.AwaitingReturn : next;
        if (next == Manager) tender.Status = "Awaiting Manager Review";
        if (next == Top) tender.Status = "Awaiting Committee Decision";
        if (next is Approved or Cancelled)
        {
            tender.Status = next;
            report.FinalizedAt = now;
            if (next == Cancelled)
            {
                var manager = tender.CommitteeMembers.Single(m => m.CommitteeType == "Evaluation" && m.MemberRole == "Sourcing Manager");
                NotifyHandoff(manager.EmployeeProfileId, tender, "Cancelled tender awaiting handoff");
            }
            var offers = await Db.Bids.Where(b => b.TenderId == id && b.SubmittedAt != null && b.Status != "Withdrawn").ToListAsync();
            foreach (var bid in offers)
            {
                var won = next == Approved && bid.Id == report.RecommendedBidId;
                bid.AwardOutcome = next == Cancelled ? "Cancelled" : won ? "Awarded" : "Unsuccessful";
                Db.Notifications.Add(new Notification
                {
                    SupplierProfileId = bid.SupplierProfileId,
                    Title = won ? "Tender awarded to you" : next == Cancelled ? "Tender cancelled" : "Bid not selected",
                    Message = tender.Title, Link = "/Bids/Details/" + bid.Id, CreatedAt = DateTime.UtcNow
                });
            }
        }
        await Db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = next == Approved ? "Tender awarded." : next == Cancelled ? "Tender cancelled." : report.Status == Top && decision == Vote ? "Vote recorded." : "Report approved and forwarded.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = Roles.SourcingManager)]
    public async Task<IActionResult> ReturnToOfficer(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await TenderRules.LockAsync(Db, id);
        var tender = await Load(id, employee.Id);
        if (tender?.EvaluationReport is not { } report || !IsAssignedManager(tender, employee.Id)) return NotFound();
        if (tender.Status != Cancelled || report.Status != TenderRules.AwaitingReturn)
            return Conflict("This tender is not awaiting a cancellation handoff.");
        report.Status = TenderRules.ReadyToRetender;
        EvaluationRules.Audit(Db, id, employee.Id, "CancellationHandoff", "Returned to sourcing officer.", DateTime.UtcNow);
        Db.TenderApprovals.Add(new TenderApproval { EvaluationReportId = report.Id,
            ApproverEmployeeProfileId = employee.Id, Decision = "Returned to sourcing officer for retender",
            DecidedAt = DateTime.UtcNow });
        NotifyHandoff(tender.CreatedByEmployeeProfileId, tender, "Cancelled tender ready to retender");
        await Db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = "Returned to the sourcing officer for a new tender.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private void NotifyHandoff(int employeeId, Tender tender, string title) =>
        Db.Notifications.Add(new Notification { EmployeeProfileId = employeeId, Title = title,
            Message = tender.Title, Link = Url.Action(nameof(Details), new { id = tender.Id }), CreatedAt = DateTime.UtcNow });
    private Task<Tender?> Load(int id, int employeeId) => Visible(employeeId).AsSplitQuery()
        .Include(t => t.CommitteeMembers.Where(m => m.CommitteeType == "Evaluation" || m.CommitteeType == "Top")).ThenInclude(m => m.EmployeeProfile)
        .Include(t => t.EvaluationCriteria)
        .Include(t => t.Bids.Where(b => b.Status == "Compliant" && b.SubmittedAt != null)).ThenInclude(b => b.SupplierProfile)
        .Include(t => t.EvaluationReport).ThenInclude(r => r!.TenderApprovals).ThenInclude(a => a.ApproverEmployeeProfile)
        .SingleOrDefaultAsync(t => t.Id == id);

    private async Task<List<EvaluationProgress>> Progress(Tender tender, List<EvaluationScore> scores)
    {
        var submissions = await Db.EvaluationSubmissions.Where(s => s.TenderId == tender.Id)
            .ToDictionaryAsync(s => s.EmployeeProfileId, s => (DateTime?)s.SubmittedAt);
        var required = tender.Bids.Count * tender.EvaluationCriteria.Count;
        return tender.CommitteeMembers.Where(m => m.CommitteeType == "Evaluation").Select(m => new EvaluationProgress(
            m.EmployeeProfile.FullName, m.MemberRole,
            EvaluationRules.CompleteCells(tender.Bids.Select(b => b.Id), tender.EvaluationCriteria,
                scores.Where(s => s.EmployeeId == m.EmployeeProfileId)),
            required, submissions.GetValueOrDefault(m.EmployeeProfileId), m.EmployeeProfileId)).ToList();
    }

    private async Task<List<SupplierEvaluationResult>> Compare(Tender tender, List<EvaluationScore> scores)
    {
        var bidders = await Db.Bids.AsNoTracking().Where(b => b.TenderId == tender.Id && b.SubmittedAt != null)
            .Include(b => b.SupplierProfile).Include(b => b.BidDocuments).AsSplitQuery().ToListAsync();
        return bidders.Select(b =>
        {
            var rows = scores.Where(s => s.BidId == b.Id).ToList();
            var eligible = b.Status == "Compliant";
            var technical = eligible ? EvaluationRules.Weighted(tender.EvaluationCriteria.Where(c => c.Type == "Technical"), rows) : (decimal?)null;
            var financial = eligible ? EvaluationRules.Weighted(tender.EvaluationCriteria.Where(c => c.Type == "Financial"), rows) : (decimal?)null;
            return new SupplierEvaluationResult(b.Id, b.SupplierProfile.CompanyName, b.Status, b.ComplianceNotes, b.TotalPrice, b.Currency,
                technical is decimal t ? EvaluationRules.Round(t) : null,
                financial is decimal f ? EvaluationRules.Round(f) : null,
                technical + financial is decimal total ? EvaluationRules.Round(total) : null,
                b.BidDocuments.Select(d => d.Id).ToList());
        }).OrderByDescending(r => r.Total).ThenBy(r => r.BidId).ToList();
    }

    private bool CanUpload(Tender tender, int employeeId, IReadOnlyList<EvaluationProgress> progress) =>
        User.IsInRole(Roles.SourcingManager) && IsAssignedManager(tender, employeeId) &&
        tender.Status == "Under Evaluation" && tender.EvaluationReport == null &&
        tender.CommitteeMembers.Select(m => m.EmployeeProfileId).Distinct().Count() == 4 &&
        EvaluationRules.CriteriaValid(tender.EvaluationCriteria.ToArray()) && progress.Count == 4 && progress.All(p => p.Complete);

    private static bool IsAssignedManager(Tender tender, int employeeId) =>
        tender.CommitteeMembers.Any(m => m.EmployeeProfileId == employeeId && m.CommitteeType == "Evaluation" && m.MemberRole == "Sourcing Manager");

    private Task<List<int>> TopVoters() => Db.EmployeeProfiles.Where(e => e.IsActive &&
        Db.UserRoles.Any(ur => ur.UserId == e.UserId && Db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.TopCommittee)))
        .Select(e => e.Id).ToListAsync();
    private string? NextStage(Tender tender, int employeeId) => tender.EvaluationReport?.Status switch
    {
        Officer when User.IsInRole(Roles.SourcingOfficer) => Manager,
        Manager when User.IsInRole(Roles.SourcingManager) && IsAssignedManager(tender, employeeId) => Top,
        Top when User.IsInRole(Roles.TopCommittee) && tender.CommitteeMembers.Any(m => m.CommitteeType == "Top" && m.EmployeeProfileId == employeeId) && tender.EvaluationReport.TenderApprovals.All(a => a.ApproverEmployeeProfileId != employeeId || a.Decision != Vote) => Approved,
        _ => null
    };
}
