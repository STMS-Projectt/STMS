using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SourcingTenderSystem.ViewModels.TenderApprovals;

public class BidComparisonItemViewModel
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

public class ApprovalHistoryItemViewModel
{
    public string ApproverName { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? SelectedSupplierName { get; set; }
    public string? Comments { get; set; }
    public DateTime DecidedAt { get; set; }
}

public class ApprovalDetailsViewModel
{
    public int EvaluationReportId { get; set; }
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string TenderDescription { get; set; } = string.Empty;
    public string TenderTerms { get; set; } = string.Empty;
    public string TenderStatus { get; set; } = string.Empty;
    public DateTime OpenDate { get; set; }
    public DateTime CloseDate { get; set; }

    public string RequisitionTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
    public string SupplierComparison { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string ReportStatus { get; set; } = string.Empty;
    public string PreparedByName { get; set; } = string.Empty;
    public DateTime PreparedAt { get; set; }

    public int RecommendedBidId { get; set; }
    public string RecommendedSupplierName { get; set; } = string.Empty;
    public decimal RecommendedPrice { get; set; }
    public string RecommendedCurrency { get; set; } = string.Empty;
    public int RecommendedDeliveryDays { get; set; }

    public List<BidComparisonItemViewModel> Bids { get; set; } = new();
    public List<ApprovalHistoryItemViewModel> ApprovalHistory { get; set; } = new();

    // Decision Form Input Fields
    [Required(ErrorMessage = "Please select a decision")]
    public string Decision { get; set; } = string.Empty;

    public bool ForwardToTopCommittee { get; set; } = false;

    public int? SelectedBidId { get; set; }

    [MaxLength(1000, ErrorMessage = "Comments cannot exceed 1000 characters")]
    public string? Comments { get; set; }

    public int? CurrentApproverProfileId { get; set; }
}
