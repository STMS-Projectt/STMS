using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace SourcingTenderSystem.Services;

// A USD reference rate applied to one bid. Saved inside the evaluation snapshot so history never changes.
public sealed record UsdConversion(decimal Rate, decimal UsdAmount, DateOnly EffectiveDate, string Source);

// Free Frankfurter v2 API (https://frankfurter.dev). No key, no paid service.
// Returns null when the rate is unavailable; callers must show a warning instead of inventing a value.
public sealed class ExchangeRates(IHttpClientFactory clients, IMemoryCache cache, ILogger<ExchangeRates> log)
{
    public const string HttpClientName = "Frankfurter";
    public const string Source = "Frankfurter v2 (api.frankfurter.dev)";

    // One reference date for every comparable bid: the tender's final closing date, never a future day.
    public static DateOnly ReferenceDate(DateTime closeDate, DateTime utcNow) =>
        DateOnly.FromDateTime(closeDate < utcNow ? closeDate : utcNow);

    public static decimal ToUsd(decimal amount, decimal rate) => Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);

    public async Task<UsdConversion?> ConvertAsync(decimal amount, string currency, DateOnly date, CancellationToken ct = default)
    {
        currency = currency.Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper)) return null;
        if (currency == "USD") return new(1m, ToUsd(amount, 1m), date, "Same currency (no conversion)");
        var rate = await RateAsync(currency, date, ct);
        return rate is { } r ? new(r.Rate, ToUsd(amount, r.Rate), r.Date, Source) : null;
    }

    private async Task<(decimal Rate, DateOnly Date)?> RateAsync(string currency, DateOnly date, CancellationToken ct)
    {
        var key = $"fx:{currency}:USD:{date:yyyy-MM-dd}";
        if (cache.TryGetValue(key, out (decimal, DateOnly) hit)) return hit;
        try
        {
            using var response = await clients.CreateClient(HttpClientName)
                .GetAsync($"v2/rate/{currency}/USD?date={date:yyyy-MM-dd}", ct);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = json.RootElement;
            if (root.GetProperty("base").GetString() != currency || root.GetProperty("quote").GetString() != "USD") return null;
            var rate = root.GetProperty("rate").GetDecimal();
            var effective = DateOnly.ParseExact(root.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (rate <= 0 || effective > date) return null;
            cache.Set(key, (rate, effective), TimeSpan.FromHours(12));
            return (rate, effective);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
            or FormatException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Exchange rate {Currency}->USD for {Date} is unavailable.", currency, date);
            return null;
        }
    }
}
