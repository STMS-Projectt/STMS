using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("TenderApprovals")]
public class TenderApproval
{
    public int Id { get; set; }
    public int EvaluationReportId { get; set; }
    public int ApproverEmployeeProfileId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public int? SelectedBidId { get; set; }
    public string? Comments { get; set; }
    public DateTime DecidedAt { get; set; }

    public EvaluationReport EvaluationReport { get; set; } = null!;
    public EmployeeProfile ApproverEmployeeProfile { get; set; } = null!;
    public Bid? SelectedBid { get; set; }
}
