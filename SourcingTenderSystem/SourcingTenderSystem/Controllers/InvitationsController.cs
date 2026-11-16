using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using SourcingTenderSystem.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;

using SourcingTenderSystem.Security;

[Authorize(Roles = Roles.SourcingOfficer)]
public class InvitationsController : Controller
{
    private readonly ApplicationDbContext _context;

    public InvitationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: INVITATIONS
    [HttpGet]
    public async Task<IActionResult> Index(int? tenderId)
    {
        var invitations = _context.Invitations.AsNoTracking()
            .Include(i => i.Tender).Include(i => i.Round)
            .Include(i => i.SupplierProfile)
            .AsQueryable();
        if (tenderId.HasValue)
        {
            var tender = await _context.Tenders.FindAsync(tenderId.Value);
            if (tender == null) return NotFound();
            ViewData["TenderTitle"] = tender.Title;
            ViewData["CanInvite"] = CanInvite(tender, DateTime.UtcNow);
            invitations = invitations.Where(i => i.TenderId == tenderId.Value);
        }

        return View(await invitations.OrderByDescending(i => i.SentAt).ThenByDescending(i => i.Id).ToListAsync());
    }
    // GET: INVITATIONS/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invitation = await _context.Invitations.AsNoTracking().Include(i => i.Tender).Include(i => i.SupplierProfile)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (invitation == null)
        {
            return NotFound();
        }

        return View(invitation);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? tenderId)
    {
        await LoadInvitationOptionsAsync(tenderId);
        return View(new Invitation { TenderId = tenderId ?? 0, ExpiresAt = DateTime.UtcNow.Date.AddDays(7) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("TenderId,ExpiresAt")] Invitation invitation, List<int> selectedSuppliers)
    {
        ModelState.Remove(nameof(Invitation.Tender));
        ModelState.Remove(nameof(Invitation.SupplierProfile));
        ModelState.Remove(nameof(Invitation.Status));

        var employeeId = await _context.EmployeeProfiles.Where(e => e.IsActive && e.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
            .Select(e => (int?)e.Id).SingleOrDefaultAsync();
        if (employeeId is null) return Forbid();
        await using var tx = await TenderRules.LockAsync(_context, invitation.TenderId);
        var ids = (selectedSuppliers ?? []).Distinct().ToList();
        var tender = await _context.Tenders.FindAsync(invitation.TenderId);
        var now = DateTime.UtcNow;
        invitation.ExpiresAt = TenderRules.AsUtc(invitation.ExpiresAt);
        if (tender is null || !CanInvite(tender, now))
            ModelState.AddModelError(nameof(Invitation.TenderId), "Invitations can only be sent while the tender is open for bidding.");
        if (ids.Count == 0 || await EligibleSuppliers(invitation.TenderId).CountAsync(s => ids.Contains(s.Id)) != ids.Count)
            ModelState.AddModelError(string.Empty, "Select approved suppliers that belong to the purchase request category.");
        if (tender != null && (invitation.ExpiresAt <= now || invitation.ExpiresAt <= tender.OpenDate))
            ModelState.AddModelError(nameof(Invitation.ExpiresAt), "Choose a UTC expiry after bidding opens and in the future.");
        if (await TenderRules.PreviouslySubmitted(_context, invitation.TenderId).AnyAsync(id => ids.Contains(id)))
            ModelState.AddModelError("", "Suppliers who have already submitted a bid cannot be invited again.");
        if (!ModelState.IsValid)
        {
            await LoadInvitationOptionsAsync(invitation.TenderId, ids);
            return View(invitation);
        }

        var current = tender!;
        if (await _context.TenderRounds.FindAsync(current.Id, current.Counter) == null)
            TenderRules.NewRound(_context, current, employeeId.Value, now);
        var prior = await TenderRules.CurrentInvitations(_context, current)
            .Where(i => ids.Contains(i.SupplierProfileId)).Select(i => i.SupplierProfileId).ToListAsync();
        var added = ids.Except(prior).ToArray();
        foreach (var supplierId in added)
        {
            _context.Invitations.Add(new Invitation
            {
                TenderId = current.Id, RoundNumber = current.Counter, SupplierProfileId = supplierId,
                Status = "Pending", SentAt = now, ExpiresAt = invitation.ExpiresAt
            });
            _context.Notifications.Add(new Notification { SupplierProfileId = supplierId,
                Title = $"Tender invitation · Round {current.Counter}", Message = current.Title,
                Link = "/SupplierWorkspace/Invitations", CreatedAt = now });
        }
        if (added.Length > 0)
            TenderRules.Audit(_context, current, employeeId, "InviteSuppliers", now, $"{added.Length} invitation attempts sent.");
        await _context.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = added.Length == 0
            ? "The selected suppliers already have invitations in this round." : "Supplier invitations saved.";
        return RedirectToAction(nameof(Index), new { tenderId = current.Id });
    }

    private IQueryable<SupplierProfile> EligibleSuppliers(int tenderId) =>
        _context.SupplierProfiles.AsNoTracking().Where(s => s.Status == SupplierStatus.Approved &&
            s.Categories.Any(c => _context.Tenders.Any(t => t.Id == tenderId && t.PurchaseRequisition.CategoryId == c.Id)));

    private async Task LoadInvitationOptionsAsync(int? tenderId, IEnumerable<int>? selected = null)
    {
        var now = DateTime.UtcNow;
        ViewData["TenderId"] = new SelectList(await _context.Tenders.AsNoTracking()
            .Where(t => t.Status == "Open" && t.OpenDate <= now && t.CloseDate > now)
            .OrderBy(t => t.Title).ToListAsync(), "Id", "Title", tenderId);
        var tender = tenderId.HasValue ? await _context.Tenders.FindAsync(tenderId.Value) : null;
        var suppliers = EligibleSuppliers(tenderId ?? 0).Where(s => false);
        if (tender != null && CanInvite(tender, now))
        {
            suppliers = EligibleSuppliers(tender.Id);
            var sent = TenderRules.CurrentInvitations(_context, tender).Select(i => i.SupplierProfileId);
            var submitted = TenderRules.PreviouslySubmitted(_context, tender.Id);
            suppliers = suppliers.Where(s => !sent.Contains(s.Id) && !submitted.Contains(s.Id));
            ViewData["RoundNumber"] = tender.Counter;
            ViewData["CategoryName"] = await _context.PurchaseRequisitions.Where(r => r.Id == tender.PurchaseRequisitionId)
                .Select(r => r.Category == null ? null : r.Category.Name).SingleOrDefaultAsync();
        }
        ViewData["TenderUnavailable"] = tenderId > 0 && (tender is null || !CanInvite(tender, now));
        ViewData["Suppliers"] = await suppliers.OrderBy(s => s.CompanyName).ToListAsync();
        ViewData["SelectedSuppliers"] = selected?.ToHashSet() ?? new HashSet<int>();
    }
    private static bool CanInvite(Tender tender, DateTime now) =>
        tender.Status == "Open" && tender.OpenDate <= now && tender.CloseDate > now;
    // Sent attempts are immutable except for the supplier's response.
}
