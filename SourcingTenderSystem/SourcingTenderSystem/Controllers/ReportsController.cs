using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

// Reports & analytics. Every number is read from the database when the page (or a download) is requested.
// The page, the Excel file and the PDF are all built from the same ReportSection.
[Authorize]
public class ReportsController : Controller
{
    private static readonly string[] Tabs = new string[] { "requests", "tenders", "suppliers", "evaluations", "decisions", "top" };
    private const int AuditRowLimit = 500;

    private readonly ApplicationDbContext _context;
    private readonly UiText _ui;
    private readonly ProtectedPdf _pdf;

    public ReportsController(ApplicationDbContext context, UiText ui, ProtectedPdf pdf)
    {
        _context = context;
        _ui = ui;
        _pdf = pdf;
    }

    // GET: /Reports?tab=tenders&from=2026-01-01&departmentId=2
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Index(ReportFilter filter)
    {
        var page = new ReportsPage();
        page.FilterError = CheckFilter(filter);
        page.Filter = filter;
        page.Section = await BuildAsync(filter, _ui);

        // Lists for the filter drop-downs
        var departments = await _context.Departments.OrderBy(d => d.Name).ToListAsync();
        foreach (var department in departments)
        {
            page.Departments.Add(new SelectListItem(department.Name, department.Id.ToString()));
        }
        var suppliers = await _context.SupplierProfiles.OrderBy(s => s.CompanyName).ToListAsync();
        foreach (var supplier in suppliers)
        {
            page.Suppliers.Add(new SelectListItem(supplier.CompanyName, supplier.Id.ToString()));
        }
        var tenders = await _context.Tenders.OrderBy(t => t.Status).ToListAsync();
        foreach (var tender in tenders)
        {
            if (!page.TenderStatuses.Contains(tender.Status)) page.TenderStatuses.Add(tender.Status);
        }

        // Tenders that received at least one submitted bid (for the "Bids & evaluation" tab)
        var tendersWithBids = await _context.Tenders
            .Where(t => t.Bids.Any(b => b.SubmittedAt != null && b.Status != "Withdrawn"))
            .OrderByDescending(t => t.Id)
            .ToListAsync();
        foreach (var tender in tendersWithBids)
        {
            page.Tenders.Add(new SelectListItem("#" + tender.Id + " · " + tender.Title, tender.Id.ToString()));
        }

        return View(page);
    }

    // GET: /Reports/Export?tab=tenders&format=excel  (same filters as the page)
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Export(ReportFilter filter, string? format)
    {
        if (!Tabs.Contains(filter.Tab)) return NotFound();
        CheckFilter(filter);
        var fileName = "stms-" + filter.Tab + "-report-" + DateTime.UtcNow.ToString("yyyyMMdd");
        return await ExportAsync(filter, format, fileName);
    }

    // GET: /Reports/TopCommittee  (site-wide statistics the Top Committee can download)
    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.TopCommittee)]
    public async Task<IActionResult> TopCommittee()
    {
        var page = new ReportsPage();
        page.Filter.Tab = "top";
        page.Section = await TopCommitteeAsync(_ui);
        return View(page);
    }

    // GET: /Reports/TopCommitteeExport?format=pdf
    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.TopCommittee)]
    public async Task<IActionResult> TopCommitteeExport(string? format)
    {
        var filter = new ReportFilter();
        filter.Tab = "top";
        var fileName = "stms-top-committee-statistics-" + DateTime.UtcNow.ToString("yyyyMMdd");
        return await ExportAsync(filter, format, fileName);
    }

    private async Task<IActionResult> ExportAsync(ReportFilter filter, string? format, string fileName)
    {
        if (format == "excel")
        {
            // Excel uses the language of the user (Arabic or English).
            var section = await BuildAsync(filter, _ui);
            var filters = await FilterSummaryAsync(filter, _ui);
            return ReportExport.ExcelFile(section, filters, _ui, fileName);
        }
        if (format == "pdf")
        {
            // PDF is always English because the PDF library cannot draw Arabic letters correctly.
            var english = new UiText("en");
            var section = await BuildAsync(filter, english);
            var filters = await FilterSummaryAsync(filter, english);
            return await ReportExport.PdfFileAsync(this, _pdf, section, filters, fileName);
        }
        return BadRequest("Choose PDF or Excel.");
    }

    private async Task<ReportSection> BuildAsync(ReportFilter filter, UiText text)
    {
        if (filter.Tab == "tenders") return await TendersAsync(filter, text);
        if (filter.Tab == "suppliers") return await SuppliersAsync(filter, text);
        if (filter.Tab == "evaluations") return await EvaluationsAsync(filter, text);
        if (filter.Tab == "decisions") return await DecisionsAsync(filter, text);
        if (filter.Tab == "top") return await TopCommitteeAsync(text);
        return await RequestsAsync(filter, text);
    }

    // Unknown tab -> first tab. Filters that a tab does not show are cleared, so they never filter it by mistake.
    // A start date after the end date is ignored and an error is shown.
    private static string? CheckFilter(ReportFilter filter)
    {
        if (!Tabs.Contains(filter.Tab)) filter.Tab = "requests";

        if (filter.Tab != "tenders" && filter.Tab != "decisions") filter.TenderStatus = null;
        if (filter.Tab == "requests" || filter.Tab == "top") filter.SupplierId = null;
        if (filter.Tab != "evaluations") filter.TenderId = null;
        if (filter.Tab == "top")
        {
            filter.From = null;
            filter.To = null;
            filter.DepartmentId = null;
        }

        if (filter.From != null && filter.To != null && filter.From > filter.To)
        {
            filter.From = null;
            filter.To = null;
            return "The end date must be on or after the start date.";
        }
        return null;
    }

    // ------------------------------------------------------------ 1. Purchase requests

    private async Task<ReportSection> RequestsAsync(ReportFilter f, UiText text)
    {
        var requests = await RequestsQuery(f)
            .Include(r => r.Department)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        var durations = await ReportRules.ApprovalDurationsAsync(_context);

        // Approval times of the requests in this report only
        var allTimes = new List<TimeSpan>();
        foreach (var request in requests)
        {
            if (durations.ContainsKey(request.Id)) allTimes.Add(durations[request.Id]);
        }
        var approved = requests.Count(r => ReportRules.ApprovedRequestStatuses.Contains(r.Status));
        var pending = requests.Count(r => ReportRules.PendingRequestStatuses.Contains(r.Status));

        var section = new ReportSection();
        section.Tab = "requests";
        section.Title = text.T("Purchase request reports");
        section.Description = text.T("Requests created in the selected period, by status and department.");
        section.Kpis.Add(Kpi(text, "Purchase requests", Number(requests.Count, text), "file", "blue", "#all", null));
        section.Kpis.Add(Kpi(text, "Approved requests", Number(approved, text), "check", "teal", "#statuses",
            ReportRules.Percent(approved, requests.Count)));
        section.Kpis.Add(Kpi(text, "Waiting for review", Number(pending, text), "clock", "amber",
            Url.Action("Requests", "AdminOperations", new { status = "Under Review" }), null));
        section.Kpis.Add(Kpi(text, "Average approval time", ReportRules.Duration(ReportRules.Average(allTimes), text),
            "calendar", "violet", "#departments", text.T("From submission to approval")));

        // Table: requests by status
        var byStatus = Table("statuses", "Requests by status", text, new string[] { "Status", "Requests", "Share" });
        var statuses = requests.Select(r => r.Status).Distinct().OrderBy(s => s).ToList();
        foreach (var status in statuses)
        {
            var count = requests.Count(r => r.Status == status);
            byStatus.Add(Url.Action("Requests", "AdminOperations", new { status = status }),
                new object?[] { text.T(status), count, ReportRules.Percent(count, requests.Count) });
        }

        // Table: requests by department
        var byDepartment = Table("departments", "Requests by department", text,
            new string[] { "Department", "Requests", "Approved", "Rejected", "Returned", "Waiting for review", "Average approval time" });
        byDepartment.Note = text.T("Approval time runs from the last submission to the approval, read from the request activity log.");
        var departments = await _context.Departments.OrderBy(d => d.Name).ToListAsync();
        foreach (var department in departments)
        {
            var departmentRequests = requests.Where(r => r.DepartmentId == department.Id).ToList();
            if (departmentRequests.Count == 0) continue;

            var times = new List<TimeSpan>();
            foreach (var request in departmentRequests)
            {
                if (durations.ContainsKey(request.Id)) times.Add(durations[request.Id]);
            }
            byDepartment.Add(Url.Action("Index", new { tab = "requests", departmentId = department.Id }), new object?[]
            {
                department.Name,
                departmentRequests.Count,
                departmentRequests.Count(r => ReportRules.ApprovedRequestStatuses.Contains(r.Status)),
                departmentRequests.Count(r => r.Status == "Rejected"),
                departmentRequests.Count(r => r.Status == "Returned"),
                departmentRequests.Count(r => ReportRules.PendingRequestStatuses.Contains(r.Status)),
                ReportRules.Duration(ReportRules.Average(times), text)
            });
        }

        // Table: every request
        var all = Table("all", "All purchase requests", text,
            new string[] { "Request", "Title", "Department", "Status", "Created (UTC)", "Approval time" });
        all.LinksOpenDialog = true;
        foreach (var request in requests)
        {
            var approvalTime = "—";
            if (durations.ContainsKey(request.Id)) approvalTime = ReportRules.Duration(durations[request.Id], text);
            all.Add(Url.Action("RequestDetails", "AdminOperations", new { id = request.Id }), new object?[]
            {
                "#" + request.Id, request.Title, request.Department.Name, text.T(request.Status), request.CreatedAt, approvalTime
            });
        }

        section.Tables.Add(byStatus);
        section.Tables.Add(byDepartment);
        section.Tables.Add(all);
        return section;
    }

    // ------------------------------------------------------------ 2. Tenders

    private async Task<ReportSection> TendersAsync(ReportFilter f, UiText text)
    {
        var tenders = await TendersQuery(f)
            .Include(t => t.PurchaseRequisition).ThenInclude(r => r.Department)
            .Include(t => t.Invitations)
            .Include(t => t.Bids)
            .OrderByDescending(t => t.Id)
            .ToListAsync();
        var tenderIds = tenders.Select(t => t.Id).ToList();
        var allDelayed = await ReportRules.DelayedEvaluationsAsync(_context, DateTime.UtcNow);
        var delayed = allDelayed.Where(d => tenderIds.Contains(d.TenderId)).ToList();

        var invited = 0;
        var bidders = 0;
        var noBids = new List<Tender>();
        foreach (var tender in tenders)
        {
            invited += ReportRules.InvitedSuppliers(tender);
            bidders += ReportRules.BiddingSuppliers(tender);
            // Bidding is finished when the tender is no longer Draft, Published or Open.
            var biddingFinished = tender.Status != "Draft" && tender.Status != "Published" && tender.Status != "Open";
            if (biddingFinished && ReportRules.BiddingSuppliers(tender) == 0) noBids.Add(tender);
        }
        var inEvaluation = tenders.Count(t => t.Status == "Closed" || t.Status == "Under Evaluation" ||
                                              t.Status == "Awaiting Manager Review" || t.Status == "Awaiting Committee Decision");

        var section = new ReportSection();
        section.Tab = "tenders";
        section.Title = text.T("Tender reports");
        section.Description = text.T("Tenders created in the selected period, supplier participation and evaluation delays.");
        section.Kpis.Add(Kpi(text, "Open tenders", Number(tenders.Count(t => t.Status == "Open"), text), "briefcase", "teal",
            Url.Action("Tenders", "AdminOperations", new { status = "Open" }), null));
        section.Kpis.Add(Kpi(text, "Closed or in evaluation", Number(inEvaluation, text), "check", "blue", "#statuses", null));
        section.Kpis.Add(Kpi(text, "Awarded tenders", Number(tenders.Count(t => t.Status == "Awarded"), text), "trophy", "violet",
            Url.Action("Tenders", "AdminOperations", new { status = "Awarded" }), null));
        section.Kpis.Add(Kpi(text, "Supplier participation rate", ReportRules.Percent(bidders, invited), "supplier", "blue",
            "#participation", text.T("Suppliers who submitted a bid out of suppliers invited")));
        section.Kpis.Add(Kpi(text, "Tenders with no bids", Number(noBids.Count, text), "warning", "amber", "#no-bids", null));
        section.Kpis.Add(Kpi(text, "Delayed evaluations", Number(delayed.Count, text), "clock", "amber", "#delays",
            text.F("Waiting more than {0} day at one stage", ReportRules.EvaluationDelayDays)));

        // Table: tenders by status
        var byStatus = Table("statuses", "Tenders by status", text, new string[] { "Status", "Tenders", "Share" });
        var statuses = tenders.Select(t => t.Status).Distinct().OrderBy(s => s).ToList();
        foreach (var status in statuses)
        {
            var count = tenders.Count(t => t.Status == status);
            byStatus.Add(Url.Action("Tenders", "AdminOperations", new { status = status }),
                new object?[] { text.T(status), count, ReportRules.Percent(count, tenders.Count) });
        }

        // Table: supplier participation in each tender
        var participation = Table("participation", "Supplier participation", text,
            new string[] { "Tender", "Department", "Status", "Invited suppliers", "Accepted", "Submitted bids", "Participation" });
        participation.LinksOpenDialog = true;
        foreach (var tender in tenders)
        {
            var tenderInvited = ReportRules.InvitedSuppliers(tender);
            if (tenderInvited == 0) continue;
            var tenderBidders = ReportRules.BiddingSuppliers(tender);
            participation.Add(Url.Action("Tender", "AdminOperations", new { id = tender.Id, panel = "invitations" }), new object?[]
            {
                tender.Title, tender.PurchaseRequisition.Department.Name, text.T(tender.Status),
                tenderInvited, ReportRules.AcceptedSuppliers(tender), tenderBidders, ReportRules.Percent(tenderBidders, tenderInvited)
            });
        }

        // Table: tenders that closed without any bid
        var withoutBids = Table("no-bids", "Tenders with no bids", text, new string[] { "Tender", "Department", "Status", "Closing date (UTC)" });
        withoutBids.LinksOpenDialog = true;
        foreach (var tender in noBids)
        {
            withoutBids.Add(Url.Action("Tender", "AdminOperations", new { id = tender.Id, panel = "overview" }), new object?[]
            {
                tender.Title, tender.PurchaseRequisition.Department.Name, text.T(tender.Status), tender.CloseDate
            });
        }

        // Table: delayed evaluations
        var delays = Table("delays", "Evaluation delays", text, new string[] { "Tender", "Waiting at", "Waiting since (UTC)", "Days waiting" });
        delays.Note = text.F("A tender is delayed when it waits more than {0} day at one stage: compliance, committee evaluation or an approval.",
            ReportRules.EvaluationDelayDays);
        delays.LinksOpenDialog = true;
        foreach (var item in delayed)
        {
            delays.Add(Url.Action("Tender", "AdminOperations", new { id = item.TenderId, panel = "overview" }), new object?[]
            {
                item.Title, text.T(item.Stage), item.Since, Math.Round(item.Days, 1)
            });
        }

        section.Tables.Add(byStatus);
        section.Tables.Add(participation);
        section.Tables.Add(withoutBids);
        section.Tables.Add(delays);
        return section;
    }

    // ------------------------------------------------------------ 3. Suppliers

    private async Task<ReportSection> SuppliersAsync(ReportFilter f, UiText text)
    {
        var now = DateTime.UtcNow;
        var warnUntil = now.AddDays(ReportRules.DocumentWarningDays);

        IQueryable<SupplierProfile> supplierQuery = _context.SupplierProfiles.Include(s => s.SupplierDocuments);
        if (f.SupplierId != null)
        {
            var supplierId = f.SupplierId.Value;
            supplierQuery = supplierQuery.Where(s => s.Id == supplierId);
        }
        var suppliers = await supplierQuery.OrderBy(s => s.CompanyName).ToListAsync();

        IQueryable<SupplierDocument> documentQuery = _context.SupplierDocuments
            .Include(d => d.SupplierProfile)
            .Where(d => d.ExpiryDate != null && d.ExpiryDate <= warnUntil);
        if (f.SupplierId != null)
        {
            var supplierId = f.SupplierId.Value;
            documentQuery = documentQuery.Where(d => d.SupplierProfileId == supplierId);
        }
        var documents = await documentQuery.OrderBy(d => d.ExpiryDate).ToListAsync();

        // Performance uses the invitations and bids inside the selected period and department.
        var invitations = await InvitationsQuery(f).ToListAsync();
        var bids = await BidsQuery(f).ToListAsync();

        var section = new ReportSection();
        section.Tab = "suppliers";
        section.Title = text.T("Supplier reports");
        section.Description = text.T("Supplier counts and documents show the current state. Performance uses invitations and bids in the selected period.");
        section.Kpis.Add(Kpi(text, "Registered suppliers", Number(suppliers.Count, text), "supplier", "blue", "#statuses", null));
        section.Kpis.Add(Kpi(text, "Approved", Number(suppliers.Count(s => s.Status == SupplierStatus.Approved), text), "check", "teal",
            Url.Action("Index", "Suppliers", new { status = SupplierStatus.Approved }), null));
        section.Kpis.Add(Kpi(text, "Suspended", Number(suppliers.Count(s => s.Status == SupplierStatus.Suspended), text), "pause", "amber",
            Url.Action("Index", "Suppliers", new { status = SupplierStatus.Suspended }), null));
        section.Kpis.Add(Kpi(text, "Expired documents", Number(documents.Count(d => d.ExpiryDate < now), text), "warning", "amber",
            "#documents", null));
        section.Kpis.Add(Kpi(text, "Documents expiring soon", Number(documents.Count(d => d.ExpiryDate >= now), text), "calendar", "violet",
            "#documents", text.F("Within {0} days", ReportRules.DocumentWarningDays)));

        // Table: suppliers by status
        var byStatus = Table("statuses", "Suppliers by status", text, new string[] { "Status", "Suppliers", "Share" });
        foreach (var status in SupplierStatus.All)
        {
            var count = suppliers.Count(s => s.Status == status);
            byStatus.Add(Url.Action("Index", "Suppliers", new { status = status }),
                new object?[] { text.T(status), count, ReportRules.Percent(count, suppliers.Count) });
        }

        // Table: legal verification (documents of each supplier)
        var verification = Table("verification", "Legal verification", text,
            new string[] { "Supplier", "Status", "Documents on file", "Expired", "Expiring soon" });
        verification.LinksOpenDialog = true;
        foreach (var supplier in suppliers)
        {
            var expired = supplier.SupplierDocuments.Count(d => d.ExpiryDate != null && d.ExpiryDate < now);
            var expiring = supplier.SupplierDocuments.Count(d => d.ExpiryDate != null && d.ExpiryDate >= now && d.ExpiryDate <= warnUntil);
            verification.Add(Url.Action("Details", "Suppliers", new { id = supplier.Id, panel = "documents" }), new object?[]
            {
                supplier.CompanyName, text.T(supplier.Status), supplier.SupplierDocuments.Count, expired, expiring
            });
        }

        // Table: expired and expiring documents
        var expiringTable = Table("documents", "Expired and expiring documents", text,
            new string[] { "Supplier", "Document", "File", "Expiry date", "Days left", "State" });
        expiringTable.LinksOpenDialog = true;
        foreach (var document in documents)
        {
            if (document.ExpiryDate == null) continue;
            var expiry = document.ExpiryDate.Value;
            var daysLeft = (int)Math.Floor((expiry - now).TotalDays);
            var state = "Expires soon";
            if (expiry < now) state = "Expired";
            expiringTable.Add(Url.Action("Details", "Suppliers", new { id = document.SupplierProfileId, panel = "documents" }), new object?[]
            {
                document.SupplierProfile.CompanyName, text.T(document.DocumentType), document.FileName ?? "—", expiry, daysLeft, text.T(state)
            });
        }

        // Table: supplier performance
        var performance = Table("performance", "Supplier performance", text, new string[]
            { "Supplier", "Invitations", "Accepted", "Bids submitted", "Compliant", "Non-compliant", "Awards", "Win rate", "Average response time" });
        performance.LinksOpenDialog = true;
        foreach (var supplier in suppliers)
        {
            var supplierInvitations = invitations.Where(i => i.SupplierProfileId == supplier.Id).ToList();
            var supplierBids = bids.Where(b => b.SupplierProfileId == supplier.Id).ToList();
            if (supplierInvitations.Count == 0 && supplierBids.Count == 0) continue;

            var responseTimes = new List<TimeSpan>();
            foreach (var invitation in supplierInvitations)
            {
                if (invitation.RespondedAt != null) responseTimes.Add(invitation.RespondedAt.Value - invitation.SentAt);
            }
            var awards = supplierBids.Count(b => b.AwardOutcome == "Awarded");
            performance.Add(Url.Action("Details", "Suppliers", new { id = supplier.Id, panel = "overview" }), new object?[]
            {
                supplier.CompanyName,
                supplierInvitations.Count,
                supplierInvitations.Count(i => i.Status == "Accepted"),
                supplierBids.Count,
                supplierBids.Count(b => b.Status == "Compliant"),
                supplierBids.Count(b => b.Status == "Non-Compliant"),
                awards,
                ReportRules.Percent(awards, supplierBids.Count),
                ReportRules.Duration(ReportRules.Average(responseTimes), text)
            });
        }

        section.Tables.Add(byStatus);
        section.Tables.Add(verification);
        section.Tables.Add(expiringTable);
        section.Tables.Add(performance);
        return section;
    }

    // ------------------------------------------------------------ 4. Bids & evaluation

    private async Task<ReportSection> EvaluationsAsync(ReportFilter f, UiText text)
    {
        var bidQuery = BidsQuery(f);
        if (f.TenderId != null)
        {
            // One tender only: a side-by-side comparison of its bids.
            var tenderId = f.TenderId.Value;
            bidQuery = bidQuery.Where(b => b.TenderId == tenderId);
        }
        var bids = await bidQuery
            .Include(b => b.Tender)
            .Include(b => b.SupplierProfile)
            .ToListAsync();
        var tenderIds = bids.Select(b => b.TenderId).Distinct().ToList();
        var reports = await _context.EvaluationReports
            .Include(r => r.Tender)
            .Include(r => r.RecommendedBid).ThenInclude(b => b.SupplierProfile)
            .Where(r => tenderIds.Contains(r.TenderId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        // The technical / financial / total scores are saved inside each evaluation report (as JSON).
        var scores = new Dictionary<int, SupplierEvaluationResult>();
        foreach (var report in reports)
        {
            foreach (var row in ReadComparison(report.SupplierComparison))
            {
                scores[row.BidId] = row;
            }
        }

        var section = new ReportSection();
        section.Tab = "evaluations";
        section.Title = text.T("Bids & evaluation reports");
        section.Description = text.T("Bids submitted in the selected period, their evaluation scores and the excluded offers.");
        section.Kpis.Add(Kpi(text, "Submitted bids", Number(bids.Count, text), "inbox", "blue", "#comparison", null));
        section.Kpis.Add(Kpi(text, "Compliant bids", Number(bids.Count(b => b.Status == "Compliant"), text), "check", "teal", "#comparison", null));
        section.Kpis.Add(Kpi(text, "Excluded bids", Number(bids.Count(b => b.Status == "Non-Compliant"), text), "warning", "amber", "#excluded", null));
        section.Kpis.Add(Kpi(text, "Evaluation reports", Number(reports.Count, text), "file", "violet", "#results", null));

        // Table: bid comparison
        var comparison = Table("comparison", "Bid comparison", text, new string[]
            { "Tender", "Supplier", "Total price", "Currency", "Delivery (days)", "Compliance", "Technical", "Financial", "Total score" });
        comparison.Note = text.T("Scores come from the evaluation report of each tender. Compare prices only within the same currency.");
        comparison.LinksOpenDialog = true;
        // Bids of the same tender stay together: highest total score first, then the lowest price.
        var orderedBids = bids
            .OrderBy(b => b.Tender.Title)
            .ThenByDescending(b => TotalScore(scores, b.Id))
            .ThenBy(b => b.TotalPrice)
            .ToList();
        foreach (var bid in orderedBids)
        {
            SupplierEvaluationResult? score = null;
            if (scores.ContainsKey(bid.Id)) score = scores[bid.Id];
            comparison.Add(Url.Action("BidDetails", "AdminOperations", new { id = bid.Id, panel = "overview" }), new object?[]
            {
                bid.Tender.Title, bid.SupplierProfile.CompanyName, bid.TotalPrice, bid.Currency, bid.DeliveryDays, text.T(bid.Status),
                score?.Technical, score?.Financial, score?.Total
            });
        }

        // Table: evaluation results (the unified report of each tender)
        var results = Table("results", "Evaluation results (unified reports)", text, new string[]
            { "Tender", "Report status", "Recommended supplier", "Technical", "Financial", "Total score", "Prepared on (UTC)" });
        foreach (var report in reports)
        {
            SupplierEvaluationResult? score = null;
            if (scores.ContainsKey(report.RecommendedBidId)) score = scores[report.RecommendedBidId];
            results.Add(Url.Action("Details", "EvaluationWorkflow", new { id = report.TenderId }), new object?[]
            {
                report.Tender.Title, text.T(report.Status), report.RecommendedBid.SupplierProfile.CompanyName,
                score?.Technical, score?.Financial, score?.Total, report.CreatedAt
            });
        }

        // Table: excluded offers (non-compliant) and the reason
        var excluded = Table("excluded", "Excluded offers", text, new string[] { "Tender", "Supplier", "Reason", "Submitted (UTC)" });
        excluded.LinksOpenDialog = true;
        foreach (var bid in bids.Where(b => b.Status == "Non-Compliant").OrderByDescending(b => b.SubmittedAt))
        {
            excluded.Add(Url.Action("BidDetails", "AdminOperations", new { id = bid.Id, panel = "overview" }), new object?[]
            {
                bid.Tender.Title, bid.SupplierProfile.CompanyName, bid.ComplianceNotes ?? "—", bid.SubmittedAt
            });
        }

        section.Tables.Add(comparison);
        section.Tables.Add(results);
        section.Tables.Add(excluded);
        return section;
    }

    // ------------------------------------------------------------ 5. Approvals & decisions

    private async Task<ReportSection> DecisionsAsync(ReportFilter f, UiText text)
    {
        IQueryable<EvaluationReport> reportQuery = _context.EvaluationReports
            .Include(r => r.Tender)
            .Include(r => r.RecommendedBid).ThenInclude(b => b.SupplierProfile)
            .Include(r => r.PreparedByEmployeeProfile);
        if (f.DepartmentId != null)
        {
            var departmentId = f.DepartmentId.Value;
            reportQuery = reportQuery.Where(r => r.Tender.PurchaseRequisition.DepartmentId == departmentId);
        }
        if (f.SupplierId != null)
        {
            var supplierId = f.SupplierId.Value;
            reportQuery = reportQuery.Where(r => r.RecommendedBid.SupplierProfileId == supplierId);
        }
        if (!string.IsNullOrEmpty(f.TenderStatus))
        {
            var status = f.TenderStatus;
            reportQuery = reportQuery.Where(r => r.Tender.Status == status);
        }
        var reports = await reportQuery.OrderByDescending(r => r.CreatedAt).ToListAsync();

        // Recommendations use the date the report was prepared. Awards use the award date.
        var recommendations = reports.Where(r => InPeriod(r.CreatedAt, f)).ToList();
        var awards = reports
            .Where(r => r.Status == "Awarded" && r.FinalizedAt != null && InPeriod(r.FinalizedAt.Value, f))
            .OrderByDescending(r => r.FinalizedAt)
            .ToList();

        var reportIds = reports.Select(r => r.Id).ToList();
        var allDecisions = await _context.TenderApprovals
            .Include(a => a.ApproverEmployeeProfile)
            .Include(a => a.EvaluationReport).ThenInclude(r => r.Tender)
            .Where(a => reportIds.Contains(a.EvaluationReportId))
            .OrderByDescending(a => a.DecidedAt)
            .ToListAsync();
        var decisions = allDecisions.Where(a => InPeriod(a.DecidedAt, f)).ToList();

        IQueryable<AuditLog> auditQuery = _context.AuditLogs.Include(a => a.EmployeeProfile).Include(a => a.SupplierProfile);
        if (f.FromUtc != null)
        {
            var from = f.FromUtc.Value;
            auditQuery = auditQuery.Where(a => a.CreatedAt >= from);
        }
        if (f.ToUtc != null)
        {
            var to = f.ToUtc.Value;
            auditQuery = auditQuery.Where(a => a.CreatedAt <= to);
        }
        if (f.SupplierId != null)
        {
            var supplierId = f.SupplierId.Value;
            auditQuery = auditQuery.Where(a => a.SupplierProfileId == supplierId);
        }
        var audit = await auditQuery.OrderByDescending(a => a.CreatedAt).Take(AuditRowLimit).ToListAsync();

        var section = new ReportSection();
        section.Tab = "decisions";
        section.Title = text.T("Approvals & decision reports");
        section.Description = text.T("Committee recommendations, approval decisions, Top Committee votes, awards and the audit trail.");
        section.Kpis.Add(Kpi(text, "Committee recommendations", Number(recommendations.Count, text), "file", "blue", "#recommendations", null));
        section.Kpis.Add(Kpi(text, "Sourcing manager decisions", Number(decisions.Count(d => IsManagerDecision(d.Decision)), text),
            "check", "teal", "#decisions", null));
        section.Kpis.Add(Kpi(text, "Top Committee votes", Number(decisions.Count(d => IsTopVote(d.Decision)), text),
            "users", "violet", "#votes", null));
        section.Kpis.Add(Kpi(text, "Awarded tenders", Number(awards.Count, text), "trophy", "amber", "#awards", null));

        // Table: recommendations of the evaluation committee
        var recommendationTable = Table("recommendations", "Committee recommendations", text,
            new string[] { "Tender", "Recommended supplier", "Prepared by", "Prepared on (UTC)", "Report status" });
        foreach (var report in recommendations)
        {
            recommendationTable.Add(Url.Action("Details", "EvaluationWorkflow", new { id = report.TenderId }), new object?[]
            {
                report.Tender.Title, report.RecommendedBid.SupplierProfile.CompanyName, report.PreparedByEmployeeProfile.FullName,
                report.CreatedAt, text.T(report.Status)
            });
        }

        // Table: every approval decision
        var decisionTable = Table("decisions", "Approval decisions", text,
            new string[] { "Date (UTC)", "Tender", "Decided by", "Decision", "Comments" });
        decisionTable.Note = text.T("Sourcing officer and sourcing manager approvals, returns and Top Committee votes.");
        foreach (var decision in decisions)
        {
            decisionTable.Add(Url.Action("Details", "EvaluationWorkflow", new { id = decision.EvaluationReport.TenderId }), new object?[]
            {
                decision.DecidedAt, decision.EvaluationReport.Tender.Title, decision.ApproverEmployeeProfile.FullName,
                text.T(DecisionLabel(decision.Decision)), decision.Comments ?? "—"
            });
        }

        // Table: Top Committee votes, one row for each tender
        var votes = Table("votes", "Top Committee voting record", text,
            new string[] { "Tender", "Approve votes", "Reject votes", "Outcome" });
        var votedTenderIds = new List<int>();
        foreach (var decision in decisions)
        {
            if (IsTopVote(decision.Decision) && !votedTenderIds.Contains(decision.EvaluationReport.TenderId))
                votedTenderIds.Add(decision.EvaluationReport.TenderId);
        }
        foreach (var tenderId in votedTenderIds)
        {
            var tenderVotes = decisions.Where(d => d.EvaluationReport.TenderId == tenderId && IsTopVote(d.Decision)).ToList();
            var tender = tenderVotes[0].EvaluationReport.Tender;
            votes.Add(Url.Action("Details", "EvaluationWorkflow", new { id = tenderId }), new object?[]
            {
                tender.Title,
                tenderVotes.Count(d => d.Decision == "Top Committee Approved"),
                tenderVotes.Count(d => d.Decision == "Top Committee Rejected"),
                text.T(tender.Status)
            });
        }

        // Table: awarded tenders (final contracts)
        var awardTable = Table("awards", "Awarded tenders (final contracts)", text,
            new string[] { "Tender", "Winning supplier", "Amount", "Currency", "Awarded on (UTC)" });
        foreach (var report in awards)
        {
            awardTable.Add(Url.Action("Details", "EvaluationWorkflow", new { id = report.TenderId }), new object?[]
            {
                report.Tender.Title, report.RecommendedBid.SupplierProfile.CompanyName, report.RecommendedBid.TotalPrice,
                report.RecommendedBid.Currency, report.FinalizedAt
            });
        }

        // Table: audit trail
        var auditTable = Table("audit", "Audit trail", text, new string[] { "Date (UTC)", "Action", "Record", "By", "Details" });
        auditTable.Note = text.F("Latest {0} entries in the selected period. The activity log has the full history with search.", AuditRowLimit);
        foreach (var entry in audit)
        {
            var by = "System";
            if (entry.EmployeeProfile != null) by = entry.EmployeeProfile.FullName;
            else if (entry.SupplierProfile != null) by = entry.SupplierProfile.CompanyName;
            auditTable.Add(null, new object?[]
            {
                entry.CreatedAt, text.T(entry.Action), text.T(entry.EntityName) + " #" + entry.EntityId, by, entry.Details ?? "—"
            });
        }

        section.Tables.Add(recommendationTable);
        section.Tables.Add(decisionTable);
        section.Tables.Add(votes);
        section.Tables.Add(awardTable);
        section.Tables.Add(auditTable);
        return section;
    }

    // ------------------------------------------------------------ 6. Top Committee statistics (whole site, no filters)

    private async Task<ReportSection> TopCommitteeAsync(UiText text)
    {
        var isAdmin = User.IsInRole(Roles.Admin);
        var suppliers = await _context.SupplierProfiles.ToListAsync();
        var bids = await _context.Bids
            .Where(b => b.SubmittedAt != null && b.Status != "Draft" && b.Status != "Withdrawn")
            .ToListAsync();
        var invitations = await _context.Invitations.ToListAsync();
        var tenders = await _context.Tenders.ToListAsync();
        var awardedReports = await _context.EvaluationReports
            .Where(r => r.Status == "Awarded" && r.FinalizedAt != null)
            .ToListAsync();

        var awarded = tenders.Count(t => t.Status == "Awarded");
        var cancelled = tenders.Count(t => t.Status == "Cancelled");
        var tendersWithBids = bids.Select(b => b.TenderId).Distinct().Count();
        var suppliersWithDealings = bids.Select(b => b.SupplierProfileId).Distinct().Count();
        double averageBids = 0;
        if (tendersWithBids > 0) averageBids = Math.Round((double)bids.Count / tendersWithBids, 1);

        var section = new ReportSection();
        section.Tab = "top";
        section.Title = text.T("Top Committee statistics");
        section.Description = text.T("Site-wide statistics on suppliers, dealings, tenders and awards. Filters do not apply to this report.");
        // Display-only cards (no link): the tables below hold the details.
        section.Kpis.Add(Kpi(text, "Registered suppliers", Number(suppliers.Count, text), "supplier", "blue", null, null));
        section.Kpis.Add(Kpi(text, "Approved suppliers", Number(suppliers.Count(s => s.Status == SupplierStatus.Approved), text),
            "check", "teal", null, null));
        section.Kpis.Add(Kpi(text, "Suppliers with dealings", Number(suppliersWithDealings, text), "briefcase", "violet", null,
            text.T("Submitted at least one bid")));
        section.Kpis.Add(Kpi(text, "Tenders", Number(tenders.Count, text), "file", "blue", null, null));
        section.Kpis.Add(Kpi(text, "Awarded tenders", Number(awarded, text), "trophy", "amber", null, null));
        section.Kpis.Add(Kpi(text, "Award rate", ReportRules.Percent(awarded, awarded + cancelled), "chart", "teal", null,
            text.T("Awarded out of finished tenders (awarded or cancelled)")));
        section.Kpis.Add(Kpi(text, "Average bids per tender", averageBids.ToString("0.#", text.DisplayCulture), "inbox", "violet", null, null));

        // Table: dealings with each supplier (most active suppliers first)
        var dealings = Table("dealings", "Dealings with each supplier", text, new string[]
            { "Supplier", "Status", "Invitations", "Bids submitted", "Awards", "First dealing (UTC)", "Last dealing (UTC)", "Dealings per month" });
        dealings.Note = text.T("A dealing is a submitted bid. Dealings per month = bids divided by the months between the first and the last bid.");
        dealings.LinksOpenDialog = true;
        var orderedSuppliers = suppliers
            .OrderByDescending(s => bids.Count(b => b.SupplierProfileId == s.Id))
            .ThenBy(s => s.CompanyName)
            .ToList();
        foreach (var supplier in orderedSuppliers)
        {
            var offers = bids.Where(b => b.SupplierProfileId == supplier.Id).OrderBy(b => b.SubmittedAt).ToList();
            var invitedCount = invitations.Count(i => i.SupplierProfileId == supplier.Id);
            if (offers.Count == 0 && invitedCount == 0) continue;

            DateTime? first = null;
            DateTime? last = null;
            double? perMonth = null;
            if (offers.Count > 0)
            {
                var firstBid = offers[0];
                var lastBid = offers[offers.Count - 1];
                if (firstBid.SubmittedAt != null && lastBid.SubmittedAt != null)
                {
                    var firstDate = firstBid.SubmittedAt.Value;
                    var lastDate = lastBid.SubmittedAt.Value;
                    var months = (lastDate.Year - firstDate.Year) * 12 + lastDate.Month - firstDate.Month + 1;
                    first = firstDate;
                    last = lastDate;
                    perMonth = Math.Round((double)offers.Count / months, 1);
                }
            }

            string? link = null;
            if (isAdmin) link = Url.Action("Details", "Suppliers", new { id = supplier.Id, panel = "overview" });
            dealings.Add(link, new object?[]
            {
                supplier.CompanyName, text.T(supplier.Status), invitedCount, offers.Count,
                offers.Count(b => b.AwardOutcome == "Awarded"), first, last, perMonth
            });
        }

        // Table: the last 12 months
        var monthly = Table("monthly", "Tenders, bids and awards by month", text,
            new string[] { "Month", "Tenders created", "Bids submitted", "Awards" });
        var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        for (var i = 11; i >= 0; i--)
        {
            var month = thisMonth.AddMonths(-i);
            var tendersCreated = tenders.Count(t => t.CreatedAt.Year == month.Year && t.CreatedAt.Month == month.Month);
            var bidsSubmitted = bids.Count(b => b.SubmittedAt != null &&
                b.SubmittedAt.Value.Year == month.Year && b.SubmittedAt.Value.Month == month.Month);
            var awards = awardedReports.Count(r => r.FinalizedAt != null &&
                r.FinalizedAt.Value.Year == month.Year && r.FinalizedAt.Value.Month == month.Month);
            monthly.Add(null, new object?[] { month.ToString("MMM yyyy", text.DisplayCulture), tendersCreated, bidsSubmitted, awards });
        }

        section.Tables.Add(dealings);
        section.Tables.Add(monthly);
        return section;
    }

    // ------------------------------------------------------------ Filtered queries

    private IQueryable<PurchaseRequisition> RequestsQuery(ReportFilter f)
    {
        IQueryable<PurchaseRequisition> query = _context.PurchaseRequisitions;
        if (f.FromUtc != null)
        {
            var from = f.FromUtc.Value;
            query = query.Where(r => r.CreatedAt >= from);
        }
        if (f.ToUtc != null)
        {
            var to = f.ToUtc.Value;
            query = query.Where(r => r.CreatedAt <= to);
        }
        if (f.DepartmentId != null)
        {
            var departmentId = f.DepartmentId.Value;
            query = query.Where(r => r.DepartmentId == departmentId);
        }
        return query;
    }

    private IQueryable<Tender> TendersQuery(ReportFilter f)
    {
        IQueryable<Tender> query = _context.Tenders;
        if (f.FromUtc != null)
        {
            var from = f.FromUtc.Value;
            query = query.Where(t => t.CreatedAt >= from);
        }
        if (f.ToUtc != null)
        {
            var to = f.ToUtc.Value;
            query = query.Where(t => t.CreatedAt <= to);
        }
        if (f.DepartmentId != null)
        {
            var departmentId = f.DepartmentId.Value;
            query = query.Where(t => t.PurchaseRequisition.DepartmentId == departmentId);
        }
        if (!string.IsNullOrEmpty(f.TenderStatus))
        {
            var status = f.TenderStatus;
            query = query.Where(t => t.Status == status);
        }
        if (f.SupplierId != null)
        {
            var supplierId = f.SupplierId.Value;
            query = query.Where(t => t.Invitations.Any(i => i.SupplierProfileId == supplierId) || t.Bids.Any(b => b.SupplierProfileId == supplierId));
        }
        return query;
    }

    // Submitted bids only (drafts stay private to the supplier; withdrawn bids are not offers).
    private IQueryable<Bid> BidsQuery(ReportFilter f)
    {
        IQueryable<Bid> query = _context.Bids.Where(b => b.SubmittedAt != null && b.Status != "Draft" && b.Status != "Withdrawn");
        if (f.FromUtc != null)
        {
            var from = f.FromUtc.Value;
            query = query.Where(b => b.SubmittedAt >= from);
        }
        if (f.ToUtc != null)
        {
            var to = f.ToUtc.Value;
            query = query.Where(b => b.SubmittedAt <= to);
        }
        if (f.DepartmentId != null)
        {
            var departmentId = f.DepartmentId.Value;
            query = query.Where(b => b.Tender.PurchaseRequisition.DepartmentId == departmentId);
        }
        if (f.SupplierId != null)
        {
            var supplierId = f.SupplierId.Value;
            query = query.Where(b => b.SupplierProfileId == supplierId);
        }
        if (!string.IsNullOrEmpty(f.TenderStatus))
        {
            var status = f.TenderStatus;
            query = query.Where(b => b.Tender.Status == status);
        }
        return query;
    }

    private IQueryable<Invitation> InvitationsQuery(ReportFilter f)
    {
        IQueryable<Invitation> query = _context.Invitations;
        if (f.FromUtc != null)
        {
            var from = f.FromUtc.Value;
            query = query.Where(i => i.SentAt >= from);
        }
        if (f.ToUtc != null)
        {
            var to = f.ToUtc.Value;
            query = query.Where(i => i.SentAt <= to);
        }
        if (f.DepartmentId != null)
        {
            var departmentId = f.DepartmentId.Value;
            query = query.Where(i => i.Tender.PurchaseRequisition.DepartmentId == departmentId);
        }
        if (f.SupplierId != null)
        {
            var supplierId = f.SupplierId.Value;
            query = query.Where(i => i.SupplierProfileId == supplierId);
        }
        if (!string.IsNullOrEmpty(f.TenderStatus))
        {
            var status = f.TenderStatus;
            query = query.Where(i => i.Tender.Status == status);
        }
        return query;
    }

    // ------------------------------------------------------------ Small helpers

    // Short text that describes the active filters (printed on the PDF and on the Excel summary sheet).
    private async Task<string> FilterSummaryAsync(ReportFilter f, UiText text)
    {
        if (f.Tab == "top") return text.T("Site-wide statistics");

        var parts = new List<string>();
        if (f.From != null || f.To != null)
        {
            var from = f.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "…";
            var to = f.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "…";
            parts.Add(text.F("Period: {0} to {1}", from, to));
        }
        if (f.DepartmentId != null)
        {
            var department = await _context.Departments.FindAsync(f.DepartmentId.Value);
            parts.Add(text.F("Department: {0}", department?.Name ?? "—"));
        }
        if (f.SupplierId != null)
        {
            var supplier = await _context.SupplierProfiles.FindAsync(f.SupplierId.Value);
            parts.Add(text.F("Supplier: {0}", supplier?.CompanyName ?? "—"));
        }
        if (!string.IsNullOrEmpty(f.TenderStatus))
        {
            parts.Add(text.F("Tender status: {0}", text.T(f.TenderStatus)));
        }
        if (f.Tab == "evaluations" && f.TenderId != null)
        {
            var tender = await _context.Tenders.FindAsync(f.TenderId.Value);
            parts.Add(text.F("Tender: {0}", tender?.Title ?? "—"));
        }

        if (parts.Count == 0) return text.T("All records");
        return string.Join("  |  ", parts);
    }

    private static bool InPeriod(DateTime date, ReportFilter f)
    {
        if (f.FromUtc != null && date < f.FromUtc.Value) return false;
        if (f.ToUtc != null && date > f.ToUtc.Value) return false;
        return true;
    }

    private static bool IsManagerDecision(string decision)
    {
        return decision == "Pending Top Committee" || decision == "Returned to sourcing officer for retender";
    }

    private static bool IsTopVote(string decision)
    {
        return decision == "Top Committee Approved" || decision == "Top Committee Rejected";
    }

    // The approval table stores the next stage as the decision; this turns it into a readable decision.
    private static string DecisionLabel(string decision)
    {
        switch (decision)
        {
            case "Pending Manager Approval":
                return "Sourcing officer approved";
            case "Pending Top Committee":
                return "Sourcing manager approved";
            case "Top Committee Approved":
                return "Top Committee approved";
            case "Top Committee Rejected":
                return "Top Committee rejected";
            default:
                return decision;
        }
    }

    // Total score of a bid, or -1 when the bid has no score yet (so it is listed after the scored bids).
    private static decimal TotalScore(Dictionary<int, SupplierEvaluationResult> scores, int bidId)
    {
        if (!scores.ContainsKey(bidId)) return -1;
        var total = scores[bidId].Total;
        if (total == null) return -1;
        return total.Value;
    }

    // Reads the supplier comparison (scores) saved as JSON in the evaluation report.
    private static List<SupplierEvaluationResult> ReadComparison(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<SupplierEvaluationResult>();
        try
        {
            var rows = JsonSerializer.Deserialize<List<SupplierEvaluationResult>>(json);
            return rows ?? new List<SupplierEvaluationResult>();
        }
        catch (JsonException)
        {
            return new List<SupplierEvaluationResult>();
        }
    }

    private static ReportTable Table(string key, string title, UiText text, string[] headers)
    {
        var table = new ReportTable();
        table.Key = key;
        table.Title = text.T(title);
        table.Headers = new string[headers.Length];
        for (var i = 0; i < headers.Length; i++)
        {
            table.Headers[i] = text.T(headers[i]);
        }
        table.Empty = text.T("No records match these filters.");
        return table;
    }

    private static ReportKpi Kpi(UiText text, string label, string value, string icon, string tone, string? link, string? hint)
    {
        var kpi = new ReportKpi();
        kpi.Label = text.T(label);
        kpi.Value = value;
        kpi.Icon = icon;
        kpi.Tone = tone;
        kpi.Link = link;
        kpi.Hint = hint;
        return kpi;
    }

    private static string Number(int value, UiText text)
    {
        return value.ToString("N0", text.DisplayCulture);
    }
}
