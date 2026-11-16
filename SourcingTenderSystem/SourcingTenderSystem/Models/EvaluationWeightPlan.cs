namespace SourcingTenderSystem.Models;

public sealed class EvaluationWeightPlan
{
    public int TenderId { get; set; }
    public int EmployeeProfileId { get; set; }
    public string CriteriaWeights { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public Tender Tender { get; set; } = null!;
    public EmployeeProfile EmployeeProfile { get; set; } = null!;
}
