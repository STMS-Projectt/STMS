using System;
using System.Collections.Generic;

namespace SourcingTenderSystem.ViewModels.TenderApprovals;

public class PendingApprovalItemViewModel
{
    public int EvaluationReportId { get; set; }
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string PreparedByName { get; set; } = string.Empty;
    public string RecommendedSupplierName { get; set; } = string.Empty;
    public decimal RecommendedBidPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string TenderStatus { get; set; } = string.Empty;
    public string ReportStatus { get; set; } = string.Empty;
    public bool HasTopCommittee { get; set; }
}

public class PendingApprovalsViewModel
{
    public string? SearchQuery { get; set; }
    public string? StatusFilter { get; set; }
    public List<PendingApprovalItemViewModel> Reports { get; set; } = new();
    public int TotalPendingCount => Reports.Count;
}
