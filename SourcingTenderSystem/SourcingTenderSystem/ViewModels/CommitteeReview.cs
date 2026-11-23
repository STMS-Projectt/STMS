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

// Supplier snapshot for the evaluation committee (CommitteeReviews/Supplier modal + DownloadSupplierProfile PDF).
// Built from the supplier's whole bid history, not only the tender under review.
public sealed record SupplierHistoryRow(int TenderId, string TenderTitle, DateTime? SubmittedAt, decimal TotalPrice, string Currency,
    int DeliveryDays, string Status, string? AwardOutcome, DateTime? AwardedAt);

public sealed class CommitteeSupplier
{
    public int BidId { get; init; }
    public int TenderId { get; init; }
    public string TenderTitle { get; init; } = "";
    public SupplierProfile Supplier { get; init; } = null!;
    public string? Email { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public int DocumentCount { get; init; }
    public int Invitations { get; init; }
    public int InvitationsAccepted { get; init; }
    public int InvitationsDeclined { get; init; }
    public IReadOnlyList<SupplierHistoryRow> Bids { get; init; } = [];

    // Who the profile is for (PDF header) and where the modal links go; set by the controller.
    public string Audience { get; set; } = "Evaluation committee";
    public bool IsRecommendedWinner { get; set; }
    public string? BackUrl { get; set; }
    public string? DownloadUrl { get; set; }

    public int BidsSubmitted => Bids.Count;
    public int Compliant => Bids.Count(b => b.Status == "Compliant");
    public int NonCompliant => Bids.Count(b => b.Status == "Non-Compliant");
    public IReadOnlyList<SupplierHistoryRow> Awards => Bids.Where(b => b.AwardOutcome == "Awarded").OrderByDescending(b => b.AwardedAt ?? b.SubmittedAt).ToList();
    public int Unsuccessful => Bids.Count(b => b.AwardOutcome == "Unsuccessful");
    // Tenders that reached a decision (won or lost); open tenders do not count against the supplier.
    public int Decided => Awards.Count + Unsuccessful;
    public decimal? WinRate => Decided == 0 ? null : Math.Round(Awards.Count * 100m / Decided, 1);
    public IReadOnlyList<(string Currency, decimal Amount)> AwardedValue => Awards.GroupBy(b => b.Currency)
        .Select(g => (g.Key, g.Sum(b => b.TotalPrice))).OrderByDescending(x => x.Item2).ToList();
    public decimal? AverageDelivery => Bids.Count == 0 ? null : Math.Round((decimal)Bids.Average(b => b.DeliveryDays), 1);
    public decimal? InvitationAcceptRate => Invitations == 0 ? null : Math.Round(InvitationsAccepted * 100m / Invitations, 1);
    public DateTime? LastAwardAt => Awards.FirstOrDefault()?.AwardedAt;
}
