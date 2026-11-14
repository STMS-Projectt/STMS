using System.ComponentModel.DataAnnotations;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.ViewModels;

public sealed record EvaluationProgress(string Employee, string Seat, int Scored, int Required, DateTime? SubmittedAt, int EmployeeId)
{
    public bool Complete => SubmittedAt.HasValue && Required > 0 && Scored == Required;
}

public sealed record EvaluationWorkflowDetails(Tender Tender, IReadOnlyList<EvaluationProgress> Progress,
    bool CanUpload, string? NextStage, IReadOnlyList<EvaluationScore> Scores);

public sealed record EvaluationScore(int EmployeeId, int BidId, int CriterionId, decimal Score, string? Notes, decimal? Taxes, decimal? Extras, decimal? TotalCost);

public sealed class FinalReportInput
{
    [Range(1, int.MaxValue)] public int RecommendedBidId { get; set; }
    [Required, StringLength(1000)] public string Summary { get; set; } = "";
    [Required] public IFormFile? File { get; set; }
}

public sealed record SupplierEvaluationResult(int BidId, string Supplier, string Compliance, string? ComplianceNotes, decimal Price, string Currency,
    decimal? Technical, decimal? Financial, decimal? Total, IReadOnlyList<int> EvidenceIds);
