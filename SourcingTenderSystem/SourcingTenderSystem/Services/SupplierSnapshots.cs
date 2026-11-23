using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Services;

/// <summary>Builds the supplier profile (modal + PDF) for one bid from the supplier's whole history.
/// The caller checks that the current user may see this bid before calling.</summary>
public static class SupplierSnapshots
{
    public static async Task<CommitteeSupplier?> BuildAsync(ApplicationDbContext db, int bidId)
    {
        var bid = await db.Bids.AsNoTracking().Include(b => b.Tender)
            .Include(b => b.SupplierProfile).ThenInclude(s => s.User)
            .Include(b => b.SupplierProfile).ThenInclude(s => s.Categories)
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.Id == bidId);
        if (bid is null) return null;
        var supplierId = bid.SupplierProfileId;

        // Every bid the supplier actually submitted (drafts and withdrawn bids are not history)
        var history = await db.Bids.AsNoTracking()
            .Where(b => b.SupplierProfileId == supplierId && b.SubmittedAt != null && b.Status != "Draft" && b.Status != "Withdrawn")
            .OrderByDescending(b => b.SubmittedAt)
            .Select(b => new SupplierHistoryRow(b.TenderId, b.Tender.Title, b.SubmittedAt, b.TotalPrice, b.Currency, b.DeliveryDays,
                b.Status, b.AwardOutcome, b.Tender.EvaluationReport != null ? b.Tender.EvaluationReport.FinalizedAt : null))
            .ToListAsync();
        var invitations = await db.Invitations.AsNoTracking().Where(i => i.SupplierProfileId == supplierId)
            .GroupBy(i => i.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();

        return new CommitteeSupplier
        {
            BidId = bid.Id,
            TenderId = bid.TenderId,
            TenderTitle = bid.Tender.Title,
            Supplier = bid.SupplierProfile,
            Email = bid.SupplierProfile.User?.Email,
            Categories = bid.SupplierProfile.Categories.Select(c => c.Name).OrderBy(n => n).ToList(),
            DocumentCount = await db.SupplierDocuments.CountAsync(d => d.SupplierProfileId == supplierId),
            Invitations = invitations.Sum(i => i.Count),
            InvitationsAccepted = invitations.Where(i => i.Status == "Accepted").Sum(i => i.Count),
            InvitationsDeclined = invitations.Where(i => i.Status == "Declined").Sum(i => i.Count),
            Bids = history
        };
    }
}
