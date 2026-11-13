using System;
using System.Collections.Generic;

namespace SourcingTenderSystem.ViewModels.TopCommittee;

public class TopCommitteeBidOptionViewModel
{
    public int BidId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsRecommended { get; set; }
}

public class TopCommitteeDetailsViewModel
{
    public int TenderId { get; set; }
    public int EvaluationReportId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string TenderDescription { get; set; } = string.Empty;
    public string TenderTerms { get; set; } = string.Empty;
    public string TenderStatus { get; set; } = string.Empty;
    public DateTime OpenDate { get; set; }
    public DateTime CloseDate { get; set; }

    public string RequisitionTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;

    // Sourcing Manager recommendation
    public string SourcingManagerName { get; set; } = string.Empty;
    public string SourcingManagerDecision { get; set; } = string.Empty;
    public string? SourcingManagerComments { get; set; }
    public DateTime? SourcingManagerDecidedAt { get; set; }

    // Unified Committee Report
    public string ReportSummary { get; set; } = string.Empty;
    public string ReportComparison { get; set; } = string.Empty;
    public string ReportRecommendation { get; set; } = string.Empty;
    public string PreparedByName { get; set; } = string.Empty;
    public DateTime ReportCreatedAt { get; set; }

    // Recommended Bid
    public int RecommendedBidId { get; set; }
    public string RecommendedSupplierName { get; set; } = string.Empty;
    public decimal RecommendedPrice { get; set; }
    public string RecommendedCurrency { get; set; } = string.Empty;
    public int RecommendedDeliveryDays { get; set; }

    // All Bids
    public List<TopCommitteeBidOptionViewModel> Bids { get; set; } = new();

    // Top Committee Members & Status
    public List<CommitteeMemberVoteRecord> CommitteeMembers { get; set; } = new();

    // Current User voting state
    public bool CurrentUserHasVoted { get; set; }
    public TopCommitteeVoteViewModel VoteForm { get; set; } = new();
}
