using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.ViewModels;

public sealed record SupplierWorkspaceSummary(string Company, string Status, int Documents,
    int Invitations, int AwaitingResponse, int DraftBids, int SentBids);

public sealed record SupplierInvitationList(IReadOnlyList<Invitation> Invitations, IReadOnlySet<int> BidTenders, DateTime Now);
