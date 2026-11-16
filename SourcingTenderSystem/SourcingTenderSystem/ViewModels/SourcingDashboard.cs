namespace SourcingTenderSystem.ViewModels;

public sealed record SourcingDashboard(string OfficerName, int PendingRequests, int Tenders,
    int Invitations, int ComplianceQueue, IReadOnlyList<SourcingRequestRow> Requests,
    IReadOnlyList<SourcingMonth> Trend, IReadOnlyDictionary<string, int> TenderStatuses);

public sealed record SourcingMonth(DateTime Month, int Requests, int Tenders);

public sealed record SourcingRequestRow(int Id, string Title, string Department, string Priority, string Status, DateTime TargetDate);
