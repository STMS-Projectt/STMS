using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("EvaluationReports")]
public class EvaluationReport
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public int RecommendedBidId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string SupplierComparison { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string? ResultsSnapshot { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public string? FileKey { get; set; }
    public string Status { get; set; } = string.Empty;
    public int PreparedByEmployeeProfileId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Tender Tender { get; set; } = null!;
    public Bid RecommendedBid { get; set; } = null!;
    public EmployeeProfile PreparedByEmployeeProfile { get; set; } = null!;
    public ICollection<TenderApproval> TenderApprovals { get; set; } = new List<TenderApproval>();
}
