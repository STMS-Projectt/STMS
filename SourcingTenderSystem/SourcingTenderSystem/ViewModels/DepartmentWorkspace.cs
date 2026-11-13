using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.ViewModels;

public sealed record DepartmentDashboard(string Department, IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<PurchaseRequisition> Recent)
{
    public int Total => Counts.Values.Sum();
    public int Count(string status) => Counts.GetValueOrDefault(status);
}

public sealed record DepartmentRequestList(string Department, IReadOnlyList<string> Statuses, string? Status,
    string? Query, IReadOnlyList<PurchaseRequisition> Requests);

public sealed record DepartmentAssignment(int TenderId, string Title, string Status, int RequestId);
