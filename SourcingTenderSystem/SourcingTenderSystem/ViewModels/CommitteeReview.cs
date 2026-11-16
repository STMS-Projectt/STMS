using System.ComponentModel.DataAnnotations;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Services;

namespace SourcingTenderSystem.ViewModels;

public sealed record CommitteeAssignment(int Id, string Title, string Status, string Seat);
public sealed record CommitteeTender(Tender Tender, bool CanEditWeights, IReadOnlyDictionary<int, int> ReviewedCriteria,
    IReadOnlyDictionary<int, decimal> Weights);
public sealed record AiRecommendationView(int TenderId, AiBidAnalysis? Analysis, string? Error);
public sealed record CommitteeBid(Bid Bid, IReadOnlyList<CommitteeScoreRow> Scores);
public sealed record CommitteeScoreRow(EvaluationCriterion Criterion, decimal? Score, string? Notes,
    decimal? Taxes, decimal? Extras, decimal WeightPercent);

public sealed record CommitteeWeights(int TenderId, string Title, IReadOnlyList<EvaluationCriterion> Criteria,
    IReadOnlyDictionary<int, decimal> Weights);

public sealed class EvaluationWeightInput
{
    public int CriterionId { get; set; }
    public bool Selected { get; set; }
    public decimal? WeightPercent { get; set; }
}

public sealed class CommitteeCheckInput
{
    public int CriterionId { get; set; }
    public bool Met { get; set; }
    [Range(typeof(decimal), "0", "999999999.99")] public decimal Taxes { get; set; }
    [Range(typeof(decimal), "0", "999999999.99")] public decimal Extras { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
}

public sealed class CommitteeCriterionInput
{
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [Required, RegularExpression("^(Technical|Financial)$")] public string Type { get; set; } = "Technical";
    [Range(1, 100)] public int MaxScore { get; set; } = 100;
    [Range(typeof(decimal), "0.01", "100")] public decimal WeightPercent { get; set; } = 50;
}
