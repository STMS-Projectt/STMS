using System.ComponentModel.DataAnnotations;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.ViewModels;

public sealed record CommitteeAssignment(int Id, string Title, string Status, string Seat);
public sealed record CommitteeTender(Tender Tender, bool CanManageCriteria, IReadOnlyDictionary<int, int> ReviewedCriteria);
public sealed record CommitteeBid(Bid Bid, IReadOnlyList<CommitteeScoreRow> Scores);
public sealed record CommitteeScoreRow(EvaluationCriterion Criterion, decimal? Score, string? Notes,
    decimal? Taxes, decimal? Extras);

public sealed class CommitteeScoreInput
{
    [Required] public decimal? Score { get; set; }
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
