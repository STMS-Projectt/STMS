using System;
using System.Collections.Generic;

namespace SourcingTenderSystem.ViewModels.TopCommittee;

public class TopCommitteePendingItemViewModel
{
    public int TenderId { get; set; }
    public int EvaluationReportId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string RecommendedSupplierName { get; set; } = string.Empty;
    public decimal RecommendedPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string ManagerDecision { get; set; } = string.Empty;
    public string? ManagerComments { get; set; }
    public DateTime SubmittedDate { get; set; }
    public int TotalMembers { get; set; }
    public int VotesRecorded { get; set; }
    public string TenderStatus { get; set; } = string.Empty;
}

public class TopCommitteePendingViewModel
{
    public string? SearchQuery { get; set; }
    public List<TopCommitteePendingItemViewModel> Tenders { get; set; } = new();
    public int TotalCount => Tenders.Count;
}
