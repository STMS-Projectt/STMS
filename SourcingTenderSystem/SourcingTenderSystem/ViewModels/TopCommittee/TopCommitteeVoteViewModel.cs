using System;
using System.ComponentModel.DataAnnotations;

namespace SourcingTenderSystem.ViewModels.TopCommittee;

public class TopCommitteeVoteViewModel
{
    public int TenderId { get; set; }
    public int EvaluationReportId { get; set; }

    [Required(ErrorMessage = "Please select your vote decision.")]
    public string Decision { get; set; } = string.Empty; // "Approved", "Rejected", "NeedMoreDetails"

    [Required(ErrorMessage = "Please select the winning bid.")]
    public int SelectedBidId { get; set; }

    [Required(ErrorMessage = "Documented justifications are required for Top Committee votes.")]
    [MinLength(10, ErrorMessage = "Please provide detailed justification (at least 10 characters).")]
    public string Comments { get; set; } = string.Empty;

    public int ApproverEmployeeProfileId { get; set; }
}
