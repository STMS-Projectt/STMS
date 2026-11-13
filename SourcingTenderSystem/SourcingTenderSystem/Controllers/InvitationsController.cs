using Microsoft.AspNetCore.Authorization;

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
            .Include(i => i.Tender)
            .Include(i => i.SupplierProfile)
            .AsQueryable();
        if (tenderId.HasValue)
        {
            var tender = await _context.Tenders.FindAsync(tenderId.Value);
            if (tender == null) return NotFound();
            ViewData["TenderTitle"] = tender.Title;
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

        var invitation = await _context.Invitations
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

        var ids = selectedSuppliers.Distinct().ToList();
        if (!await _context.Tenders.AnyAsync(t => t.Id == invitation.TenderId))
            ModelState.AddModelError(nameof(Invitation.TenderId), "Select a valid tender.");
        if (ids.Count == 0 || await EligibleSuppliers().CountAsync(s => ids.Contains(s.Id)) != ids.Count)
            ModelState.AddModelError(string.Empty, "Select at least one approved supplier.");
        if (invitation.ExpiresAt <= DateTime.UtcNow)
            ModelState.AddModelError(nameof(Invitation.ExpiresAt), "Choose an expiry date in the future.");

        if (!ModelState.IsValid)
        {
            await LoadInvitationOptionsAsync(invitation.TenderId, ids);
            return View(invitation);
        }

        var alreadyInvited = await _context.Invitations
            .Where(i => i.TenderId == invitation.TenderId && ids.Contains(i.SupplierProfileId))
            .Select(i => i.SupplierProfileId).ToListAsync();
        foreach (var supplierId in ids.Except(alreadyInvited))
        {
            _context.Invitations.Add(new Invitation
            {
                TenderId = invitation.TenderId,
                SupplierProfileId = supplierId,
                Status = "Pending",
                SentAt = DateTime.UtcNow,
                ExpiresAt = invitation.ExpiresAt
            });
        }

        await _context.SaveChangesAsync();
        TempData["Message"] = alreadyInvited.Count == ids.Count
            ? "The selected suppliers already have invitations."
            : "Supplier invitations saved.";
        return RedirectToAction(nameof(Index), new { tenderId = invitation.TenderId });
    }

    private IQueryable<SupplierProfile> EligibleSuppliers() =>
        _context.SupplierProfiles.AsNoTracking().Where(s => s.Status == SupplierStatus.Approved);

    private async Task LoadInvitationOptionsAsync(int? tenderId, IEnumerable<int>? selected = null)
    {
        ViewData["TenderId"] = new SelectList(await _context.Tenders.AsNoTracking()
            .OrderBy(t => t.Title).ToListAsync(), "Id", "Title", tenderId);
        ViewData["Suppliers"] = await EligibleSuppliers().OrderBy(s => s.CompanyName).ToListAsync();
        ViewData["SelectedSuppliers"] = selected?.ToHashSet() ?? new HashSet<int>();
    }
    // GET: INVITATIONS/Edit/5////////////////////////////////////////////////
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invitation = await _context.Invitations.FindAsync(id);
        if (invitation == null)
        {
            return NotFound();
        }

        ViewData["TenderId"] = new SelectList(_context.Tenders, "Id", "Title", invitation.TenderId);
        ViewData["SupplierProfileId"] = new SelectList(_context.SupplierProfiles, "Id", "CompanyName", invitation.SupplierProfileId);
        return View(invitation);
    }

    // POST: INVITATIONS/Edit/5
    // To protect from overposting attacks, enable the specific properties you want to bind to.
    // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int? id, [Bind("Id,TenderId,SupplierProfileId,Status,SentAt,ExpiresAt,")] Invitation invitation)
    {
        if (id != invitation.Id)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(invitation);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!InvitationExists(invitation.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            return RedirectToAction(nameof(Index));
        }
        return View(invitation);
    }

    // GET: INVITATIONS/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invitation = await _context.Invitations
            .FirstOrDefaultAsync(m => m.Id == id);
        if (invitation == null)
        {
            return NotFound();
        }

        ViewData["TenderId"] = new SelectList(_context.Tenders, "Id", "Title", invitation.TenderId);
        ViewData["SupplierProfileId"] = new SelectList(_context.SupplierProfiles, "Id", "CompanyName", invitation.SupplierProfileId);
        return View(invitation);
    }

    // POST: INVITATIONS/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int? id)
    {
        var invitation = await _context.Invitations.FindAsync(id);
        if (invitation != null)
        {
            _context.Invitations.Remove(invitation);
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private bool InvitationExists(int id)
    {
        return _context.Invitations.Any(e => e.Id == id);
    }
}
