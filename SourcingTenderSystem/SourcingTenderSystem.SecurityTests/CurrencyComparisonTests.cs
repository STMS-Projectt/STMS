using System.Text.Json;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;
using Xunit;

public sealed class CurrencyComparisonTests
{
    [Fact]
    public void Historical_amounts_are_not_ranked_against_Jordanian_dinars()
    {
        var facts = BidAiAdvisor.ComputeFacts(Snapshot(Bid(1, 20m, "JOD"), Bid(2, 10m, "USD")));
        Assert.All(facts, fact =>
        {
            Assert.False(fact.PriceComparable);
            Assert.Equal(0, fact.PriceRank);
            Assert.Equal(0m, fact.PriceGapPercent);
        });
        Assert.Equal("USD", facts[1].Currency);
        Assert.Equal(10m, facts[1].TotalPrice);
    }

    [Fact]
    public void Jordanian_dinar_bids_are_ranked_by_their_recorded_amounts()
    {
        var facts = BidAiAdvisor.ComputeFacts(Snapshot(Bid(1, 20m, "JOD"), Bid(2, 10m, "JOD")));
        Assert.Equal([2, 1], facts.Select(f => f.PriceRank));
        Assert.All(facts, f => Assert.True(f.PriceComparable));
    }

    [Fact]
    public void Old_evaluation_snapshots_keep_the_original_amount_when_obsolete_rate_fields_exist()
    {
        const string legacy = """[{"BidId":7,"Supplier":"A","Compliance":"Compliant","ComplianceNotes":null,"Price":25,"Currency":"EUR","Technical":40,"Financial":30,"Total":70,"EvidenceIds":[1],"UsdRate":1.1,"UsdEquivalent":27.5}]""";
        var row = Assert.Single(JsonSerializer.Deserialize<List<SupplierEvaluationResult>>(legacy)!);
        Assert.Equal(25m, row.Price);
        Assert.Equal("EUR", row.Currency);
    }

    private static AiBidSnapshot Bid(int id, decimal price, string currency) => new(id, "S" + id, price, currency, 10, 30, [], []);
    private static AiTenderSnapshot Snapshot(params AiBidSnapshot[] bids) => new(1, "T", "D", "Terms", [], [], bids);
}
