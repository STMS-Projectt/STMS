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
    public const string JordanianDinar = "JOD";
    public string Currency { get; set; } = JordanianDinar;
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AwardOutcome { get; set; }
    public int? ComplianceClarificationId { get; set; }
    public ClarificationRequest? ComplianceClarification { get; set; }
    public string? ComplianceNotes { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int? SubmittedRoundNumber { get; set; }
    public TenderRound? SubmittedRound { get; set; }

    // ----- Sales tax (added by the supplier) -----
    public const decimal SalesTaxRate = 0.16m;            // 16% general sales tax
    public bool IncludesSalesTax { get; set; } = true;
    [NotMapped] public decimal TaxAmount => IncludesSalesTax ? Math.Round(TotalPrice * SalesTaxRate, 2) : 0m;
    [NotMapped] public decimal TotalWithTax => TotalPrice + TaxAmount;

    // ----- Legal warranty (duration + PDF) and bank guarantee (bank, amount, expiry + PDF) -----
    // The PDF files are BidDocuments of type "Legal Warranty" / "Bank Guarantee".
    public int? LegalWarrantyMonths { get; set; }
    public string? BankName { get; set; }
    public decimal? BankGuaranteeAmount { get; set; }
    public DateTime? BankGuaranteeExpiry { get; set; }
    // Secret code in the QR link of the guarantee verification page.
    public Guid GuaranteeVerificationCode { get; set; } = Guid.NewGuid();
    [NotMapped] public bool HasBankGuarantee => !string.IsNullOrWhiteSpace(BankName);

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
