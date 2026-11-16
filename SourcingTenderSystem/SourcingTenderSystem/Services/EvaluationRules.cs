using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Services;

public static class EvaluationRules
{
    public static bool CriteriaValid(IReadOnlyCollection<EvaluationCriterion> criteria) =>
        criteria.Count > 0 && criteria.All(c => c.Type is ("Technical" or "Financial") &&
            c.MaxScore is >= 1 and <= 100 && c.WeightPercent > 0 && !string.IsNullOrWhiteSpace(c.Name)) &&
        criteria.Sum(c => c.WeightPercent) == 100m;

    public static IQueryable<Bid> Unresolved(ApplicationDbContext db, int tenderId) =>
        db.Bids.Where(b => b.TenderId == tenderId && b.SubmittedAt != null &&
            (b.Status == "Submitted" || b.Status == "Pending Clarification"));

    public static async Task<List<EvaluationScore>> Scores(ApplicationDbContext db, int tenderId)
    {
        var technical = await db.TechnicalEvaluations.Where(e => e.Bid.TenderId == tenderId && e.Bid.Status == "Compliant" &&
            e.EvaluationCriterion.TenderId == tenderId && e.EvaluationCriterion.Type == "Technical")
            .Select(e => new EvaluationScore(e.EvaluatorEmployeeProfileId, e.BidId, e.EvaluationCriteriaId, e.Score, e.Notes, null, null, null)).ToListAsync();
        var financial = await db.FinancialEvaluations.Where(e => e.Bid.TenderId == tenderId && e.Bid.Status == "Compliant" &&
            e.EvaluationCriterion.TenderId == tenderId && e.EvaluationCriterion.Type == "Financial")
            .Select(e => new EvaluationScore(e.EvaluatorEmployeeProfileId, e.BidId, e.EvaluationCriteriaId, e.Score, e.Notes,
                e.TaxesAndFees, e.ExtraCosts, e.TotalCost)).ToListAsync();
        return technical.Concat(financial).ToList();
    }

    public static int CompleteCells(IEnumerable<int> bids, IEnumerable<EvaluationCriterion> criteria, IEnumerable<EvaluationScore> scores)
    {
        var cells = scores.ToLookup(s => (s.BidId, s.CriterionId));
        return bids.Sum(b => criteria.Count(c => cells[(b, c.Id)].Take(2).ToArray() is [var row] &&
            row.Score >= 0 && row.Score <= c.MaxScore));
    }

    public static decimal Weighted(IEnumerable<EvaluationCriterion> criteria, IEnumerable<EvaluationScore> scores)
    {
        var lookup = scores.ToLookup(s => s.CriterionId);
        return criteria.Sum(c => lookup[c.Id].Average(s => s.Score) / c.MaxScore * c.WeightPercent);
    }

    public static IReadOnlyDictionary<int, decimal> EffectiveWeights(IReadOnlyCollection<EvaluationCriterion> criteria, string? saved)
    {
        if (!CriteriaValid(criteria)) return new Dictionary<int, decimal>();
        if (saved is null) return criteria.ToDictionary(c => c.Id, c => c.WeightPercent);
        try
        {
            var weights = JsonSerializer.Deserialize<Dictionary<int, decimal>>(saved);
            return weights is not null && ValidWeights(weights, criteria.Select(c => c.Id).ToHashSet())
                ? weights : new Dictionary<int, decimal>();
        }
        catch (JsonException) { return new Dictionary<int, decimal>(); }
    }

    public static bool ValidWeights(IReadOnlyDictionary<int, decimal> weights, IReadOnlySet<int> available) =>
        weights.Count > 0 && weights.All(row => available.Contains(row.Key) &&
            row.Value is > 0 and <= 100 && row.Value == Round(row.Value)) && weights.Values.Sum() == 100;

    public static async Task<IReadOnlyDictionary<int, decimal>> ReviewerWeights(ApplicationDbContext db, int tenderId, int employeeId,
        IReadOnlyCollection<EvaluationCriterion> criteria)
    {
        var saved = await db.EvaluationWeightPlans.Where(p => p.TenderId == tenderId && p.EmployeeProfileId == employeeId)
            .Select(p => p.CriteriaWeights).SingleOrDefaultAsync();
        return EffectiveWeights(criteria, saved);
    }

    // Give every evaluation seat one vote on the scale of 0–100, even when their selected criteria differ.
    public static decimal WeightedByReviewer(IEnumerable<EvaluationCriterion> criteria, IEnumerable<EvaluationScore> scores,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, decimal>> reviewerWeights)
    {
        var cells = scores.ToLookup(s => (s.EmployeeId, s.CriterionId));
        var typed = criteria.ToArray();
        return reviewerWeights.Average(reviewer => typed.Where(c => reviewer.Value.ContainsKey(c.Id))
            .Sum(c => cells[(reviewer.Key, c.Id)].Single().Score / c.MaxScore * reviewer.Value[c.Id]));
    }

    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static void Audit(ApplicationDbContext db, int tenderId, int actor, string action, string details, DateTime at) =>
        db.AuditLogs.Add(new AuditLog { EntityName = nameof(Tender), EntityId = tenderId, EmployeeProfileId = actor,
            Action = action, Details = details, CreatedAt = at });
}
