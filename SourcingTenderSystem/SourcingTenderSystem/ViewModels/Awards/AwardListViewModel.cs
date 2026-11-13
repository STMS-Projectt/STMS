using System;
using System.Collections.Generic;

namespace SourcingTenderSystem.ViewModels.Awards;

public class AwardListItemViewModel
{
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string WinningSupplierName { get; set; } = string.Empty;
    public decimal AwardAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string ApprovalLevel { get; set; } = string.Empty; // "Manager Approved" or "Committee Approved"
    public string TenderStatus { get; set; } = string.Empty;
    public bool IsAwarded => TenderStatus == "Awarded";
    public DateTime? FinalDecisionDate { get; set; }
}

public class AwardListViewModel
{
    public string? SearchQuery { get; set; }
    public string? StatusFilter { get; set; } // "All", "ReadyForAward", "Awarded"
    public List<AwardListItemViewModel> Awards { get; set; } = new();
    public int TotalReadyForAward => Awards.Count(a => !a.IsAwarded);
    public int TotalAwarded => Awards.Count(a => a.IsAwarded);
}
