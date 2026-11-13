using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize]
public sealed class SupplierWorkspaceController(ApplicationDbContext db) : Controller
{
    private IQueryable<SupplierProfile> OwnProfile() =>
        db.SupplierProfiles.Where(s => s.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier));

    private IQueryable<Invitation> OwnInvitations(int supplierId) =>
        db.Invitations.Where(i => i.SupplierProfileId == supplierId);

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = await OwnProfile().Select(s => new SupplierWorkspaceSummary(
            s.CompanyName, s.Status, s.SupplierDocuments.Count, 0, 0, 0, 0)).SingleOrDefaultAsync();
        if (model is null) return Forbid();

        if (model.Status == SupplierStatus.Approved && User.IsInRole(Roles.Supplier))
        {
            var id = await OwnProfile().Select(s => s.Id).SingleAsync();
            var now = DateTime.UtcNow;
            var invitations = OwnInvitations(id);
            var bids = db.Bids.Where(b => b.SupplierProfileId == id);
            model = model with
            {
                Invitations = await invitations.CountAsync(),
                AwaitingResponse = await invitations.CountAsync(i => i.Status == "Pending" &&
                    i.ExpiresAt > now && i.Tender.Status == "Open" &&
                    i.Tender.OpenDate <= now && i.Tender.CloseDate > now),
                DraftBids = await bids.CountAsync(b => b.Status == "Draft"),
                SentBids = await bids.CountAsync(b => b.SubmittedAt != null)
            };
        }

        return View(model);
    }

    [HttpGet, Authorize(Roles = Roles.Supplier)]
    public async Task<IActionResult> Invitations()
    {
        var supplier = await OwnProfile().AsNoTracking().SingleOrDefaultAsync();
        if (supplier?.Status != SupplierStatus.Approved) return Forbid();

        var rows = await OwnInvitations(supplier.Id).AsNoTracking().Include(i => i.Tender)
            .OrderByDescending(i => i.SentAt).ThenByDescending(i => i.Id).ToListAsync();
        var bidTenders = await db.Bids.Where(b => b.SupplierProfileId == supplier.Id)
            .Select(b => b.TenderId).ToListAsync();
        return View(new SupplierInvitationList(rows, bidTenders.ToHashSet(), DateTime.UtcNow));
    }

    [HttpPost, Authorize(Roles = Roles.Supplier)]
    public async Task<IActionResult> Respond(int id, string decision)
    {
        if (decision is not ("Accepted" or "Declined")) return BadRequest();
        var supplier = await OwnProfile().AsNoTracking().SingleOrDefaultAsync();
        if (supplier?.Status != SupplierStatus.Approved) return Forbid();

        var ownInvitation = OwnInvitations(supplier.Id).Where(i => i.Id == id);
        if (!await ownInvitation.AnyAsync()) return NotFound();

        var now = DateTime.UtcNow;
        var changed = await ownInvitation
            .Where(i => i.Status == "Pending" && i.ExpiresAt > now && i.Tender.Status == "Open" &&
                i.Tender.OpenDate <= now && i.Tender.CloseDate > now &&
                i.SupplierProfile.Status == SupplierStatus.Approved)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, decision)
                .SetProperty(i => i.RespondedAt, now));
        if (changed == 0)
            TempData["InvitationError"] = "This invitation is no longer available for a response.";
        else
            TempData["Message"] = decision == "Accepted" ? "Invitation accepted. You can now prepare a bid." : "Invitation declined.";

        return RedirectToAction(nameof(Invitations));
    }
}
