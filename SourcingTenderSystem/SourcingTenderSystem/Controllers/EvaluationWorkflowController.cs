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
        var visible = Visible(employee.Id).AsNoTracking();
        var tenders = await visible.Include(t => t.EvaluationReport).OrderByDescending(t => t.Id).ToListAsync();
        if (User.IsInRole(Roles.TopCommittee))
        {
            var awaiting = await visible.Where(t => t.EvaluationReport!.Status == Top &&
                t.EvaluationReport.ResultsSnapshot != null &&
                !t.EvaluationReport.TenderApprovals.Any(a => a.ApproverEmployeeProfileId == employee.Id && a.Decision == Vote))
                .Select(t => t.Id).ToListAsync();
            ViewData["AwaitingMyVote"] = awaiting.ToHashSet();
        }
        return View(tenders);
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
        var weights = await WeightSets(tender);
        scores = ActiveScores(scores, weights);
        var progress = await Progress(tender, scores, weights);
        ViewBag.Comparison = tender.EvaluationReport?.SupplierComparison is { Length: > 0 } comparison &&
            tender.EvaluationReport.ResultsSnapshot != null
            ? System.Text.Json.JsonSerializer.Deserialize<List<SupplierEvaluationResult>>(comparison)
            : progress.Count == 4 && progress.All(p => p.Complete) ? await Compare(tender, scores, weights) : null;
        return View(new EvaluationWorkflowDetails(tender, progress, CanUpload(tender, employee.Id, progress),
            NextStage(tender, employee.Id), scores, weights));
    }

    // The sourcing officer closes the evaluation once all four reviewers have submitted; the report then goes to the sourcing manager.
    [HttpPost, Authorize(Roles = Roles.SourcingOfficer), RequestSizeLimit(SupplierFileStore.MaxBytes + 65536)]
    public async Task<IActionResult> Upload(int id, FinalReportInput input, CancellationToken cancellationToken)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await TenderRules.LockAsync(Db, id, cancellationToken);
        var tender = await Load(id, employee.Id);
        if (tender is null) return NotFound();
        var scores = await EvaluationRules.Scores(Db, id);
        var weights = await WeightSets(tender);
        scores = ActiveScores(scores, weights);
        var progress = await Progress(tender, scores, weights);
        if (!CanUpload(tender, employee.Id, progress) || await EvaluationRules.Unresolved(Db, id).AnyAsync()) return Conflict("All four members must finish first.");
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(input.Summary) || !tender.Bids.Any(b => b.Id == input.RecommendedBidId))
        {
            TempData["WorkflowError"] = "Select a compliant bid and provide a summary and PDF report.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var comparison = await Compare(tender, scores, weights);
        var snapshot = System.Text.Json.JsonSerializer.Serialize(new {
            tender.Id, tender.Title, tender.Description, tender.Terms, tender.OpenDate, tender.CloseDate,
            Criteria = tender.EvaluationCriteria.Select(c => new { c.Id, c.Name, c.Type, c.MaxScore, c.WeightPercent }),
            Scores = scores, ReviewerWeights = weights, Submissions = progress, Suppliers = comparison, input.RecommendedBidId,
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
                FileKey = key, Status = Manager, PreparedByEmployeeProfileId = employee.Id, CreatedAt = DateTime.UtcNow
            });
            tender.Status = "Awaiting Manager Review";
            var manager = tender.CommitteeMembers.FirstOrDefault(m => m.CommitteeType == "Evaluation" && m.MemberRole == "Sourcing Manager");
            if (manager is not null)
                NotifyHandoff(manager.EmployeeProfileId, tender, "Evaluation report ready for your approval",
                    $"The sourcing officer closed the evaluation of tender #{tender.Id} \"{tender.Title}\" and sent the final report for your approval.",
                    Url.Action(nameof(Details), new { id = tender.Id }));
            EvaluationRules.Audit(Db, id, employee.Id, "EvaluationReportPrepared", $"Recommended bid #{input.RecommendedBidId}", DateTime.UtcNow);
            await Db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            files.Delete(key);
            throw;
        }
        TempData["Message"] = "Evaluation closed. The report is with the sourcing manager.";
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

    // ------------------------------------------------------------ Winner brief (AI, advisory only)

    /// <summary>Dialog shell: the AI brief itself is loaded by <see cref="WinnerBrief"/> so the dialog opens at once.</summary>
    [HttpGet, Authorize(Roles = Roles.TopCommittee + "," + Roles.Admin)]
    public async Task<IActionResult> Winner(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var tender = await Visible(employee.Id).AsNoTracking().Include(t => t.EvaluationReport)
            .ThenInclude(r => r!.RecommendedBid).ThenInclude(b => b.SupplierProfile).SingleOrDefaultAsync(t => t.Id == id);
        if (tender?.EvaluationReport is not { ResultsSnapshot: not null } report || !HasWinner(report.Status)) return NotFound();
        return View(tender);
    }

    [HttpGet, Authorize(Roles = Roles.TopCommittee + "," + Roles.Admin)]
    public async Task<IActionResult> WinnerBrief(int id, [FromServices] WinnerAiBriefing briefing, CancellationToken cancellationToken)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var input = await WinnerInputAsync(id, employee.Id);
        if (input is null) return NotFound();
        return PartialView("_WinnerBrief", await briefing.GetOrCreateAsync(input, cancellationToken));
    }

    [HttpGet, Authorize(Roles = Roles.TopCommittee + "," + Roles.Admin)]
    public async Task<IActionResult> PrintWinnerBrief(int id, [FromServices] WinnerAiBriefing briefing, CancellationToken cancellationToken)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var input = await WinnerInputAsync(id, employee.Id);
        if (input is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return View(await briefing.GetOrCreateAsync(input, cancellationToken));
    }

    [HttpGet, Authorize(Roles = Roles.TopCommittee + "," + Roles.Admin)]
    public async Task<IActionResult> DownloadWinnerBrief(int id, [FromServices] WinnerAiBriefing briefing,
        [FromServices] ProtectedPdf pdf, [FromServices] ILogger<EvaluationWorkflowController> logger, CancellationToken cancellationToken)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();
        var input = await WinnerInputAsync(id, employee.Id);
        if (input is null) return NotFound();
        var brief = await briefing.GetOrCreateAsync(input, cancellationToken, language: "en");
        try
        {
            using var document = WinnerBriefPdf.Build(brief);
            pdf.AddWatermark(document, User, DateTime.UtcNow);
            var bytes = await pdf.Render(document, null);
            Response.Headers.CacheControl = "private, no-store";
            return File(bytes, "application/pdf", $"winner-brief-tender-{id}.pdf");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not build the winner brief PDF for tender {TenderId}", id);
            return StatusCode(422, new UiText().T("Could not prepare the winner brief PDF."));
        }
    }

    private static bool HasWinner(string status) => status is Top or Approved;

    private async Task<WinnerBriefInput?> WinnerInputAsync(int id, int employeeId)
    {
        var tender = await Visible(employeeId).AsNoTracking().AsSplitQuery()
            .Include(t => t.PurchaseRequisition).ThenInclude(r => r.Items)
            .Include(t => t.EvaluationCriteria)
            .Include(t => t.EvaluationReport)
            .SingleOrDefaultAsync(t => t.Id == id);
        if (tender?.EvaluationReport is not { ResultsSnapshot: not null } report || !HasWinner(report.Status)) return null;
        var bids = await Db.Bids.AsNoTracking().AsSplitQuery()
            .Where(b => b.TenderId == id && b.SubmittedAt != null && b.Status == "Compliant")
            .Include(b => b.SupplierProfile).ThenInclude(s => s.Categories)
            .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequisitionItem)
            .Include(b => b.BidDocuments)
            .OrderBy(b => b.Id).ToListAsync();
        var winner = bids.FirstOrDefault(b => b.Id == report.RecommendedBidId);
        if (winner is null) return null;
        var scores = (string.IsNullOrWhiteSpace(report.SupplierComparison) ? null
            : System.Text.Json.JsonSerializer.Deserialize<List<SupplierEvaluationResult>>(report.SupplierComparison)) ?? [];
        var ranked = scores.Where(r => r.Total != null && bids.Any(b => b.Id == r.BidId))
            .OrderByDescending(r => r.Total).ThenBy(r => r.BidId).Select(r => r.BidId).ToList();
        var previousAwards = await Db.Bids.CountAsync(b => b.SupplierProfileId == winner.SupplierProfileId &&
            b.TenderId != id && b.AwardOutcome == "Awarded");
        var items = tender.PurchaseRequisition.Items.OrderBy(i => i.Id).ToList();
        var supplier = winner.SupplierProfile;
        return new WinnerBriefInput(tender.Id, tender.Title, tender.Description, report.Status, report.Summary,
            winner.Id,
            new WinnerSupplierProfile(supplier.CompanyName, supplier.Address,
                supplier.Categories.Select(c => c.Name).OrderBy(n => n).ToList(), supplier.CreatedAt, previousAwards),
            items.Select(i => new AiRequestedItem(i.Id, i.ItemName, i.Quantity, i.Unit, i.Specifications)).ToList(),
            tender.EvaluationCriteria.OrderBy(c => c.Type).ThenBy(c => c.Id)
                .Select(c => new AiCriterion(c.Name, c.Type, c.WeightPercent, c.MaxScore)).ToList(),
            bids.Select(b =>
            {
                var row = scores.FirstOrDefault(r => r.BidId == b.Id);
                var rank = ranked.IndexOf(b.Id);
                return new WinnerBriefBid(b.Id, b.SupplierProfile.CompanyName, b.TotalPrice, b.Currency,
                    b.DeliveryDays, b.ValidityDays, row?.Technical, row?.Financial, row?.Total,
                    rank < 0 ? bids.Count : rank + 1,
                    items.Count(i => b.BidItems.Any(l => l.PurchaseRequisitionItemId == i.Id && l.Quantity > 0)),
                    b.BidItems.OrderBy(l => l.Id).Select(l => new AiBidLine(l.PurchaseRequisitionItemId,
                        l.PurchaseRequisitionItem.ItemName, l.Quantity, l.UnitPrice, l.Notes)).ToList(),
                    b.BidDocuments.OrderBy(d => d.Id).Select(d => $"{d.DocumentType}: {d.FileName}").ToList());
            }).ToList());
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
    public async Task<IActionResult> Approve(int id, string? comments, string expectedStage, string? vote, string? password, bool confirmDecision, [FromServices] ProtectedPdf pdf, [FromServices] AwardEmailService mailer)
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
            !(await Progress(tender, await EvaluationRules.Scores(Db, id), await WeightSets(tender))).All(p => p.Complete) ||
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
                NotifyHandoff(manager.EmployeeProfileId, tender, "Tender cancelled: handoff required",
                    $"Tender #{tender.Id} \"{tender.Title}\" was cancelled by the Top Committee. Reason: {comments!.Trim()} " +
                    "Return it to the sourcing officer so a new tender can be created for the same purchase requisition.",
                    Url.Action(nameof(Details), new { id = tender.Id }));
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
        if (next == Approved) await QueueAwardEmailsAsync(tender, report.RecommendedBidId, mailer);
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
        var reason = report.TenderApprovals.Where(a => a.Decision == "Top Committee Rejected")
            .OrderByDescending(a => a.DecidedAt).Select(a => a.Comments).FirstOrDefault();
        NotifyHandoff(tender.CreatedByEmployeeProfileId, tender, "Tender cancelled: create a new tender",
            $"Tender #{tender.Id} \"{tender.Title}\" was cancelled by the Top Committee. Reason: {reason ?? "not recorded"} " +
            $"Create a NEW tender for the same purchase requisition #{tender.PurchaseRequisitionId}. The cancelled tender and its history are preserved.",
            Url.Action("Create", "Tenders", new { purchaseRequisitionId = tender.PurchaseRequisitionId }));
        await Db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = "Returned to the sourcing officer for a new tender.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Collects the award facts + every person involved, then sends the e-mails in the background
    // (the AI text can take a while, so the Top Committee member does not wait for it).
    private async Task QueueAwardEmailsAsync(Tender tender, int winnerBidId, AwardEmailService mailer)
    {
        var winner = await Db.Bids.AsNoTracking().Include(b => b.SupplierProfile).ThenInclude(s => s.User)
            .Include(b => b.BidDocuments).SingleAsync(b => b.Id == winnerBidId);
        var requisition = await Db.PurchaseRequisitions.AsNoTracking()
            .Where(r => r.Id == tender.PurchaseRequisitionId)
            .Select(r => new { r.CreatedByEmployeeProfileId, r.ReviewedByEmployeeProfileId }).SingleAsync();

        // Officer who created the tender, requester + reviewer of the requisition, evaluation and top committees
        var employeeIds = tender.CommitteeMembers.Select(m => m.EmployeeProfileId)
            .Append(tender.CreatedByEmployeeProfileId).Append(requisition.CreatedByEmployeeProfileId)
            .Concat(requisition.ReviewedByEmployeeProfileId is int reviewer ? [reviewer] : Array.Empty<int>())
            .Distinct().ToList();
        var recipients = await Db.EmployeeProfiles.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id) && e.IsActive && e.User.Email != null)
            .Select(e => new AwardRecipient(e.User.Email!, e.FullName, false)).ToListAsync();
        if (winner.SupplierProfile.User.Email is { Length: > 0 } supplierEmail)
            recipients.Add(new AwardRecipient(supplierEmail, winner.SupplierProfile.CompanyName, true));

        var guaranteeFolder = Path.Combine(HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "App_Data", "BidDocuments");
        var data = new AwardEmailData(tender.Id, tender.Title, winner.SupplierProfile.CompanyName,
            winner.TotalPrice, winner.TaxAmount, winner.TotalWithTax, winner.Currency, winner.DeliveryDays,
            winner.LegalWarrantyMonths, winner.BankName,
            winner.BankGuaranteeAmount, winner.BankGuaranteeExpiry,
            Url.Action(nameof(Details), "EvaluationWorkflow", new { id = tender.Id }, Request.Scheme)!,
            winner.HasBankGuarantee ? Url.Action("Index", "GuaranteeVerify",
                new { code = winner.GuaranteeVerificationCode.ToString("N") }, Request.Scheme) : null,
            Url.Action("Upload", "BidDocuments", new { bidId = winner.Id }, Request.Scheme)!,
            // The warranty / guarantee documents already uploaded are attached to the winner's e-mail
            winner.BidDocuments.Where(d => BidDocument.AfterAwardTypes.Contains(d.DocumentType))
                .Select(d => Path.Combine(guaranteeFolder, Path.GetFileName(d.FilePath))).ToList());

        _ = Task.Run(() => mailer.SendAsync(data, recipients));
    }

    private void NotifyHandoff(int employeeId, Tender tender, string title, string message, string? link) =>
        Db.Notifications.Add(new Notification { EmployeeProfileId = employeeId, Title = title,
            Message = message, Link = link, CreatedAt = DateTime.UtcNow });
    private Task<Tender?> Load(int id, int employeeId) => Visible(employeeId).AsSplitQuery()
        .Include(t => t.CommitteeMembers.Where(m => m.CommitteeType == "Evaluation" || m.CommitteeType == "Top")).ThenInclude(m => m.EmployeeProfile)
        .Include(t => t.EvaluationCriteria)
        .Include(t => t.Bids.Where(b => b.Status == "Compliant" && b.SubmittedAt != null)).ThenInclude(b => b.SupplierProfile)
        .Include(t => t.EvaluationReport).ThenInclude(r => r!.TenderApprovals).ThenInclude(a => a.ApproverEmployeeProfile)
        .SingleOrDefaultAsync(t => t.Id == id);

    private async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<int, decimal>>> WeightSets(Tender tender)
    {
        var saved = await Db.EvaluationWeightPlans.AsNoTracking().Where(p => p.TenderId == tender.Id)
            .ToDictionaryAsync(p => p.EmployeeProfileId, p => p.CriteriaWeights);
        var criteria = tender.EvaluationCriteria.ToArray();
        return tender.CommitteeMembers.Where(m => m.CommitteeType == "Evaluation").Select(m => m.EmployeeProfileId)
            .Distinct().ToDictionary(id => id, id => EvaluationRules.EffectiveWeights(criteria, saved.GetValueOrDefault(id)));
    }

    private static List<EvaluationScore> ActiveScores(IEnumerable<EvaluationScore> scores,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, decimal>> weights) =>
        scores.Where(s => weights.TryGetValue(s.EmployeeId, out var selected) && selected.ContainsKey(s.CriterionId)).ToList();

    private async Task<List<EvaluationProgress>> Progress(Tender tender, List<EvaluationScore> scores,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, decimal>> weights)
    {
        var submissions = await Db.EvaluationSubmissions.Where(s => s.TenderId == tender.Id)
            .ToDictionaryAsync(s => s.EmployeeProfileId, s => (DateTime?)s.SubmittedAt);
        return tender.CommitteeMembers.Where(m => m.CommitteeType == "Evaluation").Select(m =>
        {
            var selected = tender.EvaluationCriteria.Where(c => weights[m.EmployeeProfileId].ContainsKey(c.Id)).ToArray();
            return new EvaluationProgress(m.EmployeeProfile.FullName, m.MemberRole,
                EvaluationRules.CompleteCells(tender.Bids.Select(b => b.Id), selected, scores.Where(s => s.EmployeeId == m.EmployeeProfileId)),
                tender.Bids.Count * selected.Length, submissions.GetValueOrDefault(m.EmployeeProfileId), m.EmployeeProfileId);
        }).ToList();
    }

    private async Task<List<SupplierEvaluationResult>> Compare(Tender tender, List<EvaluationScore> scores,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, decimal>> weights)
    {
        var bidders = await Db.Bids.AsNoTracking().Where(b => b.TenderId == tender.Id && b.SubmittedAt != null)
            .Include(b => b.SupplierProfile).Include(b => b.BidDocuments).AsSplitQuery().ToListAsync();
        return bidders.Select(b =>
        {
            var rows = scores.Where(s => s.BidId == b.Id).ToList();
            var eligible = b.Status == "Compliant";
            var technical = eligible ? EvaluationRules.WeightedByReviewer(tender.EvaluationCriteria.Where(c => c.Type == "Technical"), rows, weights) : (decimal?)null;
            var financial = eligible ? EvaluationRules.WeightedByReviewer(tender.EvaluationCriteria.Where(c => c.Type == "Financial"), rows, weights) : (decimal?)null;
            return new SupplierEvaluationResult(b.Id, b.SupplierProfile.CompanyName, b.Status, b.ComplianceNotes, b.TotalPrice, b.Currency,
                technical is decimal t ? EvaluationRules.Round(t) : null,
                financial is decimal f ? EvaluationRules.Round(f) : null,
                technical + financial is decimal total ? EvaluationRules.Round(total) : null,
                b.BidDocuments.Select(d => d.Id).ToList());
        }).OrderByDescending(r => r.Total).ThenBy(r => r.BidId).ToList();
    }

    private bool CanUpload(Tender tender, int employeeId, IReadOnlyList<EvaluationProgress> progress) =>
        User.IsInRole(Roles.SourcingOfficer) &&
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
        // Legacy stage: reports created before the officer closed evaluations directly still need the officer's approval.
        Officer when User.IsInRole(Roles.SourcingOfficer) => Manager,
        Manager when User.IsInRole(Roles.SourcingManager) && IsAssignedManager(tender, employeeId) => Top,
        Top when User.IsInRole(Roles.TopCommittee) && tender.CommitteeMembers.Any(m => m.CommitteeType == "Top" && m.EmployeeProfileId == employeeId) && tender.EvaluationReport.TenderApprovals.All(a => a.ApproverEmployeeProfileId != employeeId || a.Decision != Vote) => Approved,
        _ => null
    };
}
