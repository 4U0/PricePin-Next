using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Feeds;

namespace PricePin.Core.Market;

public enum FearGreedLevel { ExtremeFear, Fear, Neutral, Greed, ExtremeGreed }

/// <summary>The crypto market at a glance. Any part may be missing if its source is down.</summary>
public sealed record MarketOverview(
    decimal? TotalMarketCap,
    string MarketCapCurrency,
    decimal? MarketCapChange24h,
    decimal? BtcDominance,
    decimal? EthDominance,
    int? FearGreed,
    DateTimeOffset FetchedAt)
{
    public FearGreedLevel? FearGreedLevel => FearGreed switch
    {
        null => null,
        <= 24 => Market.FearGreedLevel.ExtremeFear,
        <= 44 => Market.FearGreedLevel.Fear,
        <= 55 => Market.FearGreedLevel.Neutral,
        <= 75 => Market.FearGreedLevel.Greed,
        _ => Market.FearGreedLevel.ExtremeGreed,
    };

    public bool IsEmpty => TotalMarketCap is null && BtcDominance is null && FearGreed is null;
}

/// <summary>
/// Total market cap and dominance from CoinGecko's public /global endpoint, and the
/// Crypto Fear &amp; Greed Index from alternative.me. Both are free, need no key and work worldwide.
/// </summary>
public sealed class MarketOverviewService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private MarketOverview? _cached;

    public async Task<MarketOverview> GetAsync(string quote, CancellationToken ct = default)
    {
        var currency = quote.ToUpperInvariant() is "USDT" or "USDC" ? "usd" : quote.ToLowerInvariant();
        if (_cached is { } c && DateTimeOffset.UtcNow - c.FetchedAt < CacheFor && c.MarketCapCurrency == currency)
            return c;

        var global = TryGetAsync("https://api.coingecko.com/api/v3/global", ct);
        var fng = TryGetAsync("https://api.alternative.me/fng/?limit=1", ct);
        await Task.WhenAll(global, fng).ConfigureAwait(false);

        var overview = Parse(await global.ConfigureAwait(false), await fng.ConfigureAwait(false), currency, DateTimeOffset.UtcNow);
        if (!overview.IsEmpty)
            _cached = overview;
        return overview;
    }

    internal static MarketOverview Parse(JsonElement? global, JsonElement? fng, string currency, DateTimeOffset now)
    {
        decimal? cap = null, capChange = null, btc = null, eth = null;
        var capCurrency = currency;
        if (global is { } g && g.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("total_market_cap", out var caps))
            {
                if (caps.TryGetProperty(currency, out var v) && v.ValueKind == JsonValueKind.Number)
                    cap = v.GetDecimal();
                else if (caps.TryGetProperty("usd", out var usd) && usd.ValueKind == JsonValueKind.Number)
                {
                    cap = usd.GetDecimal();
                    capCurrency = "usd";
                }
            }
            if (data.TryGetProperty("market_cap_change_percentage_24h_usd", out var ch) && ch.ValueKind == JsonValueKind.Number)
                capChange = ch.GetDecimal();
            if (data.TryGetProperty("market_cap_percentage", out var pct))
            {
                if (pct.TryGetProperty("btc", out var b) && b.ValueKind == JsonValueKind.Number) btc = b.GetDecimal();
                if (pct.TryGetProperty("eth", out var e) && e.ValueKind == JsonValueKind.Number) eth = e.GetDecimal();
            }
        }

        int? index = null;
        if (fng is { } f && f.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array
            && list.GetArrayLength() > 0 && list[0].TryGetProperty("value", out var value)
            && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            index = Math.Clamp(parsed, 0, 100);

        return new MarketOverview(cap, capCurrency, capChange, btc, eth, index, now);
    }

    private static async Task<JsonElement?> TryGetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await Http.Client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
