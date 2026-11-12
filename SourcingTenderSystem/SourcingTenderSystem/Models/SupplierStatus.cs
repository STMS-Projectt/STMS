namespace SourcingTenderSystem.Models;

public static class SupplierStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Suspended = "Suspended";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Pending, Approved, Rejected, Suspended });
}
