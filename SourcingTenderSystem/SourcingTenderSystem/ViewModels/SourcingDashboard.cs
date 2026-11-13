namespace SourcingTenderSystem.ViewModels;

public sealed record SourcingDashboard(string OfficerName, int PendingRequests, int Tenders,
    int Invitations, int ComplianceQueue, IReadOnlyList<SourcingRequestRow> Requests);

public sealed record SourcingRequestRow(int Id, string Title, string Department, string Priority, string Status);
