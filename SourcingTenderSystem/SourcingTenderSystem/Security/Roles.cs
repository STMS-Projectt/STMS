namespace SourcingTenderSystem.Security;

public static class Roles
{
    public const string Admin = "Admin";
    public const string SourcingOfficer = "SourcingOfficer";
    public const string EvaluationCommittee = "EvaluationCommittee";
    public const string SourcingManager = "SourcingManager";
    public const string TechnicalCommittee = "TechnicalCommittee";
    public const string FinancialCommittee = "FinancialCommittee";
    public const string TopCommittee = "TopCommittee";
    public const string Reviewers = SourcingManager + "," + TechnicalCommittee + "," + FinancialCommittee + ",RequestingDepartment";
    public const string Supplier = "Supplier";
    public static IReadOnlyList<string> EmployeeRoles { get; } = Array.AsReadOnly(new[]
    {
        Admin, "RequestingDepartment", SourcingOfficer, EvaluationCommittee, SourcingManager, TechnicalCommittee, FinancialCommittee, TopCommittee
    });
}
