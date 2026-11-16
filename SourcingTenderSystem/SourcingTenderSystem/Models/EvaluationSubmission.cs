namespace SourcingTenderSystem.Models;

public sealed class EvaluationSubmission
{
    public int TenderId { get; set; }
    public int EmployeeProfileId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public Tender Tender { get; set; } = null!;
    public EmployeeProfile EmployeeProfile { get; set; } = null!;
}
