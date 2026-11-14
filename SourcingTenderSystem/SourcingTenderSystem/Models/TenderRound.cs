namespace SourcingTenderSystem.Models;

public sealed class TenderRound
{
    public int TenderId { get; set; }
    public int Number { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int? OfficerId { get; set; }
    public string? ReopenReason { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosingOutcome { get; set; }
    public int? EligibleSuppliersAtClose { get; set; }
    public bool Legacy { get; set; }
    public Tender Tender { get; set; } = null!;
    public EmployeeProfile? Officer { get; set; }
}
