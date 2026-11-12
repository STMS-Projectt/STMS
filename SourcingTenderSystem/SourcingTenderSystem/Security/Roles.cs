namespace SourcingTenderSystem.Security;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Supplier = "Supplier";
    public static IReadOnlyList<string> EmployeeRoles { get; } = Array.AsReadOnly(new[]
    {
        Admin, "RequestingDepartment", "SourcingOfficer", "EvaluationCommittee", "SourcingManager", "TopCommittee"
    });
}
