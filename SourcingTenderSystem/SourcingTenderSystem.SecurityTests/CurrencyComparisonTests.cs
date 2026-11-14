using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;
using Xunit;

public sealed class CurrencyComparisonTests
{
    [Fact]
    public async Task Converts_with_decimal_math_and_records_rate_date_and_source()
    {
        var api = new FakeRates("""{"date":"2026-11-10","base":"JOD","quote":"USD","rate":1.4104}""");
        var fx = Service(api);
        var result = await fx.ConvertAsync(10_000m, "jod", new DateOnly(2026, 11, 10));
        Assert.NotNull(result);
        Assert.Equal(1.4104m, result.Rate);
        Assert.Equal(14_104.00m, result.UsdAmount);
        Assert.Equal(new DateOnly(2026, 11, 10), result.EffectiveDate);
        Assert.Equal(ExchangeRates.Source, result.Source);
        Assert.Equal("/v2/rate/JOD/USD", api.LastUri!.AbsolutePath);
        Assert.Equal("?date=2026-11-10", api.LastUri.Query);
        // Same currency/date is cached: one reference rate for all comparable bids.
        Assert.Equal(13_398.80m, (await fx.ConvertAsync(9_500m, "JOD", new DateOnly(2026, 11, 10)))!.UsdAmount);
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task Usd_needs_no_lookup()
    {
        var api = new FakeRates("{}");
        var result = await Service(api).ConvertAsync(12_000m, "USD", new DateOnly(2026, 11, 10));
        Assert.Equal(12_000m, result!.UsdAmount);
        Assert.Equal(1m, result.Rate);
        Assert.Equal(0, api.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"status":404,"message":"not found"}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"status":422,"message":"invalid currency: XYZ"}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"date":"2026-11-10","base":"EUR","quote":"USD","rate":1.12}""")]   // wrong pair
    [InlineData(HttpStatusCode.OK, """{"date":"2026-11-12","base":"JOD","quote":"USD","rate":1.41}""")]   // later than requested
    [InlineData(HttpStatusCode.OK, """{"date":"2026-11-10","base":"JOD","quote":"USD","rate":0}""")]
    public async Task Unavailable_rates_return_null_instead_of_a_guess(HttpStatusCode status, string body)
    {
        var api = new FakeRates(body, status);
        Assert.Null(await Service(api).ConvertAsync(10_000m, "JOD", new DateOnly(2026, 11, 10)));
    }

    [Fact]
    public async Task Network_failure_returns_null()
    {
        var api = new FakeRates("") { Throw = true };
        Assert.Null(await Service(api).ConvertAsync(10_000m, "EUR", new DateOnly(2026, 11, 10)));
        Assert.Null(await Service(api).ConvertAsync(10_000m, "../x", new DateOnly(2026, 11, 10)));
    }

    [Fact]
    public void Reference_date_is_the_closing_date_never_the_future()
    {
        var now = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 11, 10), ExchangeRates.ReferenceDate(new DateTime(2026, 11, 10, 17, 0, 0), now));
        Assert.Equal(new DateOnly(2026, 11, 14), ExchangeRates.ReferenceDate(new DateTime(2026, 12, 1), now));
    }

    [Fact]
    public void Ai_facts_never_rank_raw_amounts_across_currencies()
    {
        AiBidSnapshot Bid(int id, decimal price, string currency, decimal? usd) =>
            new(id, "S" + id, price, currency, 10, 30, [], [], usd);
        // Raw 10,000 JOD is the smallest number, but 12,000 USD is the cheapest offer.
        var ranked = BidAiAdvisor.ComputeFacts(Snapshot(Bid(1, 10_000m, "JOD", 14_104m), Bid(2, 13_000m, "EUR", 14_300m), Bid(3, 12_000m, "USD", 12_000m)));
        Assert.Equal([2, 3, 1], ranked.Select(f => f.PriceRank));
        Assert.All(ranked, f => Assert.True(f.PriceComparable));
        Assert.Equal(17.5m, ranked[0].PriceGapPercent);

        var missing = BidAiAdvisor.ComputeFacts(Snapshot(Bid(1, 10_000m, "JOD", null), Bid(2, 13_000m, "EUR", 14_300m)));
        Assert.All(missing, f => { Assert.False(f.PriceComparable); Assert.Equal(0, f.PriceRank); Assert.Equal(0m, f.PriceGapPercent); });

        var single = BidAiAdvisor.ComputeFacts(Snapshot(Bid(1, 20m, "JOD", null), Bid(2, 10m, "JOD", null)));
        Assert.Equal([2, 1], single.Select(f => f.PriceRank));
        Assert.All(single, f => Assert.Null(f.UsdEquivalent));
    }

    [Fact]
    public void Legacy_comparison_snapshots_stay_readable_without_rates()
    {
        const string legacy = """[{"BidId":7,"Supplier":"A","Compliance":"Compliant","ComplianceNotes":null,"Price":25,"Currency":"JOD","Technical":40,"Financial":30,"Total":70,"EvidenceIds":[1]}]""";
        var row = Assert.Single(JsonSerializer.Deserialize<List<SupplierEvaluationResult>>(legacy)!);
        Assert.Equal(25m, row.Price);
        Assert.Equal("JOD", row.Currency);
        Assert.Null(row.UsdEquivalent);
        Assert.Null(row.UsdRate);

        var saved = row with { UsdRate = 1.4104m, UsdEquivalent = 35.26m, RateDate = new DateOnly(2026, 11, 10), RateSource = ExchangeRates.Source };
        var restored = JsonSerializer.Deserialize<SupplierEvaluationResult>(JsonSerializer.Serialize(saved))!;
        Assert.Equal(saved.UsdRate, restored.UsdRate);
        Assert.Equal(saved.RateDate, restored.RateDate);
        Assert.Equal(25m, restored.Price);
    }

    private static AiTenderSnapshot Snapshot(params AiBidSnapshot[] bids) => new(1, "T", "D", "Terms", [], [], bids);

    private static ExchangeRates Service(FakeRates api) =>
        new(api, new MemoryCache(new MemoryCacheOptions()), NullLogger<ExchangeRates>.Instance);

    private sealed class FakeRates(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls;
        public bool Throw;
        public Uri? LastUri;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("https://api.frankfurter.dev/") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            LastUri = request.RequestUri;
            if (Throw) throw new HttpRequestException("offline");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
