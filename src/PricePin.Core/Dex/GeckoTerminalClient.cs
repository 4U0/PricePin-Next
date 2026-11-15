using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Feeds;
using PricePin.Core.History;

namespace PricePin.Core.Dex;

/// <summary>
/// Price history for DEX pools from GeckoTerminal's free public API (no key, about 10
/// requests a minute), used for DEX tokens' sparklines and charts. Prices are in US dollars.
/// </summary>
public static class GeckoTerminalClient
{
    /// <param name="timeframe">"minute", "hour" or "day".</param>
    /// <param name="aggregate">Candle size in that unit: minute 1/5/15, hour 1/4/12, day 1.</param>
    /// <returns>Oldest-first candles, or null if GeckoTerminal doesn't have the pool.</returns>
    public static async Task<IReadOnlyList<Candle>?> GetCandlesAsync(
        string chain, string poolAddress, string timeframe, int aggregate, int limit, CancellationToken ct = default)
    {
        var network = DexChains.GeckoTerminalNetwork(chain);
        var url = $"https://api.geckoterminal.com/api/v2/networks/{Uri.EscapeDataString(network)}/pools/{Uri.EscapeDataString(poolAddress)}" +
                  $"/ohlcv/{timeframe}?aggregate={aggregate}&limit={limit}&currency=usd&token=base";
        for (var attempt = 0; ; attempt++)
        {
            await WaitTurnAsync(ct).ConfigureAwait(false);
            using var response = await Http.Client.GetAsync(url, ct).ConfigureAwait(false);
            // The free API allows only a few calls in a burst; wait as asked and try once more.
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt == 0)
            {
                // Retry-After can be missing or 0; wait at least a few seconds either way.
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(15);
                wait = wait < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait;
                await Task.Delay(wait, ct).ConfigureAwait(false);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                return null;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
            return ParseOhlcv(json);
        }
    }

    // GeckoTerminal's free API allows about 10 calls a minute. Calls past that wait their turn
    // instead of failing, so a chart, a few sparklines and quick search all still load.
    public const int CallsPerMinute = 10;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Queue<DateTime> RecentCalls = new();

    private static async Task WaitTurnAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (RecentCalls.Count > 0 && DateTime.UtcNow - RecentCalls.Peek() >= TimeSpan.FromMinutes(1))
                RecentCalls.Dequeue();
            if (RecentCalls.Count >= CallsPerMinute)
            {
                var wait = RecentCalls.Peek() + TimeSpan.FromMinutes(1) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                RecentCalls.Dequeue();
            }
            RecentCalls.Enqueue(DateTime.UtcNow);
        }
        finally
        {
            Gate.Release();
        }
    }

    // {"data":{"attributes":{"ohlcv_list":[[timeSec, open, high, low, close, volumeUsd], ...]}}} newest first
    internal static IReadOnlyList<Candle>? ParseOhlcv(JsonElement json)
    {
        if (!json.TryGetProperty("data", out var data)
            || !data.TryGetProperty("attributes", out var attributes)
            || !attributes.TryGetProperty("ohlcv_list", out var list)
            || list.ValueKind != JsonValueKind.Array)
            return null;
        var candles = new List<Candle>();
        foreach (var k in list.EnumerateArray())
        {
            // A candle with a missing or unreadable value is skipped rather than losing the chart.
            if (k.ValueKind != JsonValueKind.Array || k.GetArrayLength() < 6
                || Num(k[0]) is not { } t || Num(k[1]) is not { } o || Num(k[2]) is not { } h
                || Num(k[3]) is not { } l || Num(k[4]) is not { } c)
                continue;
            candles.Add(new Candle(DateTimeOffset.FromUnixTimeSeconds((long)t), o, h, l, c, Num(k[5]) ?? 0));
        }
        return candles.OrderBy(c => c.Time).ToList();
    }

    private static decimal? Num(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetDecimal(out var d) => d,
        JsonValueKind.Number when double.IsFinite(e.GetDouble()) && Math.Abs(e.GetDouble()) < 1e27 => (decimal)e.GetDouble(),
        JsonValueKind.String when decimal.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    /// <summary>The raw answer for one request, for the feed probe.</summary>
    internal static async Task<string> DescribeAsync(string chain, string poolAddress, string timeframe, int aggregate, int limit)
    {
        var url = $"https://api.geckoterminal.com/api/v2/networks/{Uri.EscapeDataString(DexChains.GeckoTerminalNetwork(chain))}/pools/{Uri.EscapeDataString(poolAddress)}" +
                  $"/ohlcv/{timeframe}?aggregate={aggregate}&limit={limit}&currency=usd&token=base";
        using var response = await Http.Client.GetAsync(url).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return $"HTTP {(int)response.StatusCode}: {(text.Length > 200 ? text[..200] : text)}";
    }

    /// <summary>Turns dollar candles into another currency; volume becomes an amount of the token.</summary>
    public static IReadOnlyList<Candle> Convert(IReadOnlyList<Candle> usd, decimal usdToQuote) =>
        usd.Select(c => new Candle(c.Time, c.Open * usdToQuote, c.High * usdToQuote, c.Low * usdToQuote, c.Close * usdToQuote,
                c.Close > 0 ? c.Volume / c.Close : 0))
            .ToList();
}
