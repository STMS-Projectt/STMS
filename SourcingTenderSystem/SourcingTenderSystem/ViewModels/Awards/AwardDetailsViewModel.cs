using System;
using System.Collections.Generic;

namespace SourcingTenderSystem.ViewModels.Awards;

public class AwardItemDetailViewModel
{
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal TotalLinePrice => UnitPrice * Quantity;
    public string Specifications { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class AwardApprovalStepViewModel
{
    public string ApproverName { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public DateTime DecidedAt { get; set; }
}

public class AwardDetailsViewModel
{
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string TenderDescription { get; set; } = string.Empty;
    public string TenderTerms { get; set; } = string.Empty;
    public string TenderStatus { get; set; } = string.Empty;
    public bool IsAwarded => TenderStatus == "Awarded";

    public string RequisitionTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;

    // Supplier Info
    public int SupplierId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public string CommercialRegNo { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    // Winning Offer Info
    public int BidId { get; set; }
    public decimal TotalAwardAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; }
    public DateTime? SubmittedAt { get; set; }

    // Line Items
    public List<AwardItemDetailViewModel> LineItems { get; set; } = new();

    // Approvals History
    public List<AwardApprovalStepViewModel> ApprovalsTrail { get; set; } = new();
}
