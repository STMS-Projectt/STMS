using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class AdminOperationsController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public Task<IActionResult> Requests(string? q, int page = 1, string? status = null) => Register("Purchase requests",
        "Track requests, originating departments, and review decisions.", nameof(Requests), q, page, status,
        db.PurchaseRequisitions.Select(r => new AdminRecord { Id = r.Id, Name = r.Title, Context = r.Department.Name, Status = r.Status, Date = r.CreatedAt, TenderId = null }));

    [HttpGet]
    public Task<IActionResult> Tenders(string? q, int page = 1, string? status = null) => Register("Tenders",
        "Follow sourcing, invitations, committee assignments, and evaluation.", nameof(Tenders), q, page, status,
        db.Tenders.Select(t => new AdminRecord { Id = t.Id, Name = t.Title, Context = t.PurchaseRequisition.Department.Name, Status = t.Status, Date = t.CreatedAt, TenderId = t.Id }));

    [HttpGet]
    public Task<IActionResult> Invitations(string? q, int page = 1, string? status = null) => Register("Supplier invitations",
        "Track invitations and supplier responses across tenders.", nameof(Invitations), q, page, status,
        db.Invitations.Select(i => new AdminRecord { Id = i.Id, Name = i.SupplierProfile.CompanyName, Context = i.Tender.Title, Status = i.Status, Date = i.SentAt, TenderId = i.TenderId }));

    [HttpGet]
    public Task<IActionResult> Bids(string? q, int page = 1, string? status = null) => Register("Supplier bids",
        "Track bid and compliance status. Supplier drafts stay private.", nameof(Bids), q, page, status,
        db.Bids.Where(b => b.SubmittedAt != null && b.Status != "Draft").Select(b =>
            new AdminRecord { Id = b.Id, Name = b.SupplierProfile.CompanyName, Context = b.Tender.Title, Status = b.Status, Date = b.SubmittedAt, TenderId = b.TenderId }));

    [HttpGet]
    public Task<IActionResult> Activity(string? q, int page = 1) => Register("Activity log",
        "Review recorded workflow and account activity.", nameof(Activity), q, page, null,
        db.AuditLogs.Select(a => new AdminRecord { Id = a.Id, Name = a.Action, Context =
            (a.EmployeeProfile != null ? a.EmployeeProfile.FullName : a.SupplierProfile != null ? a.SupplierProfile.CompanyName : "System")
                + " · " + a.EntityName + " #" + a.EntityId, Status = a.Details, Date = a.CreatedAt }));

    [HttpGet]
    public async Task<IActionResult> RequestDetails(int id)
    {
        var request = await db.PurchaseRequisitions.AsNoTracking().AsSplitQuery().Include(r => r.Department)
            .Include(r => r.CreatedByEmployeeProfile).Include(r => r.ReviewedByEmployeeProfile)
            .Include(r => r.Items).Include(r => r.Tenders).SingleOrDefaultAsync(r => r.Id == id);
        return request is null ? NotFound() : View("Request", request);
    }

    [HttpGet]
    public async Task<IActionResult> Tender(int id)
    {
        var tender = await db.Tenders.AsNoTracking().AsSplitQuery()
            .Include(t => t.PurchaseRequisition).ThenInclude(r => r.Department)
            .Include(t => t.CommitteeMembers).ThenInclude(m => m.EmployeeProfile)
            .Include(t => t.EvaluationReport).SingleOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        var invitations = await db.Invitations.Where(i => i.TenderId == id).OrderByDescending(i => i.Id)
            .Select(i => new AdminRecord { Id = i.Id, Name = i.SupplierProfile.CompanyName, Context = null, Status = i.Status, Date = i.SentAt, TenderId = null }).ToListAsync();
        var bids = await db.Bids.Where(b => b.TenderId == id && b.SubmittedAt != null && b.Status != "Draft").OrderByDescending(b => b.Id)
            .Select(b => new AdminRecord { Id = b.Id, Name = b.SupplierProfile.CompanyName, Context = null, Status = b.Status, Date = b.SubmittedAt, TenderId = null }).ToListAsync();
        return View(new AdminTenderView(tender, invitations, bids));
    }

    [HttpGet]
    public async Task<IActionResult> InvitationDetails(int id)
    {
        var invitation = await db.Invitations.AsNoTracking().Include(i => i.Tender)
            .Include(i => i.SupplierProfile).SingleOrDefaultAsync(i => i.Id == id);
        return invitation is null ? NotFound() : View(invitation);
    }

    [HttpGet]
    public async Task<IActionResult> BidDetails(int id)
    {
        var bid = await db.Bids.AsNoTracking().Include(b => b.Tender).Include(b => b.SupplierProfile)
            .Include(b => b.BidItems).ThenInclude(i => i.PurchaseRequisitionItem)
            .SingleOrDefaultAsync(b => b.Id == id && b.SubmittedAt != null && b.Status != "Draft");
        return bid is null ? NotFound() : View(bid);
    }

    [HttpGet]
    public Task<IActionResult> RequestQr(int id) => RecordQr(db.PurchaseRequisitions
        .Where(r => r.Id == id).Select(r => r.Title), nameof(RequestDetails), id);

    [HttpGet]
    public Task<IActionResult> TenderQr(int id) => RecordQr(db.Tenders
        .Where(t => t.Id == id).Select(t => t.Title), nameof(Tender), id);

    private async Task<IActionResult> RecordQr(IQueryable<string> query, string action, int id)
    {
        var title = await query.SingleOrDefaultAsync();
        if (title is null) return NotFound();
        ViewData["RecordTitle"] = title;
        ViewData["RecordQrOpen"] = true;
        return View("Qr", Url.Action(action, new { id }));
    }

    private async Task<IActionResult> Register(string title, string description, string action, string? q, int page, string? status,
        IQueryable<AdminRecord> query)
    {
        const int size = 25;
        page = Math.Clamp(page, 1, int.MaxValue / size);
        q = q?.Trim();
        if (!string.IsNullOrEmpty(q))
            query = query.Where(r => r.Name.Contains(q) || r.Context != null && r.Context.Contains(q) ||
                r.Status != null && r.Status.Contains(q));
        var statuses = action == nameof(Activity) ? new List<string>() : await query
            .Where(r => r.Status != null && r.Status != "").Select(r => r.Status!)
            .Distinct().OrderBy(s => s).ToListAsync();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        var rows = await query.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
            .Skip((page - 1) * size).Take(size + 1).ToListAsync();
        return View("Register", new AdminRegister(title, description, action, q, rows.Take(size).ToList(), page, rows.Count > size, status, statuses));
    }
}
