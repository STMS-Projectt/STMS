using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("Bids")]
public class Bid
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public int SupplierProfileId { get; set; }
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ComplianceNotes { get; set; }
    public DateTime? SubmittedAt { get; set; }

    public Tender Tender { get; set; } = null!;
    public SupplierProfile SupplierProfile { get; set; } = null!;
    public ICollection<BidItem> BidItems { get; set; } = new List<BidItem>();
    public ICollection<BidDocument> BidDocuments { get; set; } = new List<BidDocument>();
    public ICollection<TechnicalEvaluation> TechnicalEvaluations { get; set; } = new List<TechnicalEvaluation>();
    public ICollection<FinancialEvaluation> FinancialEvaluations { get; set; } = new List<FinancialEvaluation>();
    public ICollection<EvaluationReport> RecommendedInEvaluationReports { get; set; } = new List<EvaluationReport>();
    public ICollection<TenderApproval> SelectedInTenderApprovals { get; set; } = new List<TenderApproval>();
    public ICollection<ClarificationRequest> ClarificationRequests { get; set; } = new List<ClarificationRequest>();
}
