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
    public Task<IActionResult> Requests(string? q, int page = 1) => Register("Purchase requests",
        "Track requests, originating departments, and review decisions.", nameof(Requests), q, page,
        db.PurchaseRequisitions.Select(r => new AdminRecord { Id = r.Id, Name = r.Title, Context = r.Department.Name, Status = r.Status, Date = r.CreatedAt, TenderId = null }));

    [HttpGet]
    public Task<IActionResult> Tenders(string? q, int page = 1) => Register("Tenders",
        "Follow sourcing, invitations, committee assignments, and evaluation.", nameof(Tenders), q, page,
        db.Tenders.Select(t => new AdminRecord { Id = t.Id, Name = t.Title, Context = t.PurchaseRequisition.Department.Name, Status = t.Status, Date = t.CreatedAt, TenderId = t.Id }));

    [HttpGet]
    public Task<IActionResult> Invitations(string? q, int page = 1) => Register("Supplier invitations",
        "Track invitations and supplier responses across tenders.", nameof(Invitations), q, page,
        db.Invitations.Select(i => new AdminRecord { Id = i.Id, Name = i.SupplierProfile.CompanyName, Context = i.Tender.Title, Status = i.Status, Date = i.SentAt, TenderId = i.TenderId }));

    [HttpGet]
    public Task<IActionResult> Bids(string? q, int page = 1) => Register("Supplier bids",
        "Track bid and compliance status. Supplier drafts stay private.", nameof(Bids), q, page,
        db.Bids.Where(b => b.SubmittedAt != null && b.Status != "Draft").Select(b =>
            new AdminRecord { Id = b.Id, Name = b.SupplierProfile.CompanyName, Context = b.Tender.Title, Status = b.Status, Date = b.SubmittedAt, TenderId = b.TenderId }));

    [HttpGet]
    public Task<IActionResult> Activity(string? q, int page = 1) => Register("Activity log",
        "Review recorded workflow and account activity.", nameof(Activity), q, page,
        db.AuditLogs.Select(a => new AdminRecord { Id = a.Id, Name = a.Action, Context =
            (a.EmployeeProfile != null ? a.EmployeeProfile.FullName : a.SupplierProfile != null ? a.SupplierProfile.CompanyName : "System")
                + " · " + a.EntityName + " #" + a.EntityId, Status = a.Details, Date = a.CreatedAt }));

    [HttpGet]
    public async Task<IActionResult> RequestDetails(int id)
    {
        var request = await db.PurchaseRequisitions.AsNoTracking().AsSplitQuery().Include(r => r.Department)
            .Include(r => r.CreatedByEmployeeProfile).Include(r => r.ReviewedByEmployeeProfile)
            .Include(r => r.Items).Include(r => r.Tender).SingleOrDefaultAsync(r => r.Id == id);
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

    private async Task<IActionResult> Register(string title, string description, string action, string? q, int page,
        IQueryable<AdminRecord> query)
    {
        const int size = 25;
        page = Math.Clamp(page, 1, int.MaxValue / size);
        q = q?.Trim();
        if (!string.IsNullOrEmpty(q))
            query = query.Where(r => r.Name.Contains(q) || r.Context != null && r.Context.Contains(q) ||
                r.Status != null && r.Status.Contains(q));
        var rows = await query.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
            .Skip((page - 1) * size).Take(size + 1).ToListAsync();
        return View("Register", new AdminRegister(title, description, action, q, rows.Take(size).ToList(), page, rows.Count > size));
    }
}
