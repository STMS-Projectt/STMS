using System;
using System.Collections.Generic;

namespace SourcingTenderSystem.ViewModels.TopCommittee;

public class CommitteeMemberVoteRecord
{
    public string MemberName { get; set; } = string.Empty;
    public string MemberRole { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? SelectedSupplierName { get; set; }
    public decimal? SelectedBidPrice { get; set; }
    public string? Currency { get; set; }
    public string? Justification { get; set; }
    public DateTime? VotedAt { get; set; }
    public bool HasVoted => VotedAt.HasValue;
}

public class TopCommitteeResultsViewModel
{
    public int TenderId { get; set; }
    public int EvaluationReportId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string TenderStatus { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string RecommendedSupplierName { get; set; } = string.Empty;
    public decimal RecommendedPrice { get; set; }
    public string Currency { get; set; } = string.Empty;

    public int TotalMembersCount { get; set; }
    public int ApproveCount { get; set; }
    public int RejectCount { get; set; }
    public int NeedMoreDetailsCount { get; set; }

    public bool IsFinalized => ApproveCount + RejectCount >= TotalMembersCount && TotalMembersCount > 0;
    public string FinalOutcome { get; set; } = string.Empty;

    public List<CommitteeMemberVoteRecord> MemberVotes { get; set; } = new();
}
