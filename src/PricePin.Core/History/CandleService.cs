using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Feeds;
using PricePin.Core.Market;

namespace PricePin.Core.History;

public enum ChartRange { Day, Week, Month, Year }

public sealed record Candle(DateTimeOffset Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>Candles for the coin detail chart, and which exchange they came from.</summary>
public sealed record CandleSeries(IReadOnlyList<Candle> Candles, string Source);

/// <summary>
/// Price history for the detail chart over a day, week, month or year. Like the live feed it
/// tries Binance, then Coinbase, then Kraken, so it works wherever the user is. Results are
/// cached briefly so flipping between ranges doesn't hit the exchanges every time.
/// </summary>
public sealed class CandleService
{
    private sealed record Spec(string Binance, int BinanceLimit, int CoinbaseSeconds, int KrakenMinutes, TimeSpan Span, TimeSpan CacheFor);

    private static readonly Dictionary<ChartRange, Spec> Specs = new()
    {
        [ChartRange.Day] = new("15m", 96, 900, 15, TimeSpan.FromDays(1), TimeSpan.FromMinutes(1)),
        [ChartRange.Week] = new("1h", 168, 3600, 60, TimeSpan.FromDays(7), TimeSpan.FromMinutes(5)),
        [ChartRange.Month] = new("4h", 180, 21600, 240, TimeSpan.FromDays(30), TimeSpan.FromMinutes(10)),
        [ChartRange.Year] = new("1d", 365, 86400, 1440, TimeSpan.FromDays(365), TimeSpan.FromHours(1)),
    };

    private readonly Dictionary<(string, ChartRange), (DateTime At, CandleSeries Series)> _cache = [];

    /// <summary>Oldest-first candles, or null if no exchange has the coin in that currency.</summary>
    public async Task<CandleSeries?> GetAsync(MarketSymbol symbol, ChartRange range, CancellationToken ct = default)
    {
        var spec = Specs[range];
        var key = (symbol.ToString(), range);
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < spec.CacheFor)
                return hit.Series;
        }

        (string Name, Func<Task<IReadOnlyList<Candle>?>> Fetch)[] sources =
        [
            ("Binance", () => FromBinanceAsync(symbol, spec, ct)),
            ("Coinbase", () => FromCoinbaseAsync(symbol, spec, ct)),
            ("Kraken", () => FromKrakenAsync(symbol, spec, ct)),
        ];
        foreach (var (name, fetch) in sources)
        {
            try
            {
                if (await fetch().ConfigureAwait(false) is { Count: > 1 } candles)
                {
                    var series = new CandleSeries(candles, name);
                    lock (_cache)
                        _cache[key] = (DateTime.UtcNow, series);
                    return series;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                           or InvalidOperationException or FormatException or IndexOutOfRangeException
                                       && !ct.IsCancellationRequested)
            {
                // Try the next exchange.
            }
        }
        return null;
    }

    private static async Task<IReadOnlyList<Candle>?> FromBinanceAsync(MarketSymbol s, Spec spec, CancellationToken ct)
    {
        var symbol = BinanceFeed.ToExchangeSymbol(s).ToUpperInvariant();
        var json = await GetJsonAsync(
            $"https://data-api.binance.vision/api/v3/klines?symbol={symbol}&interval={spec.Binance}&limit={spec.BinanceLimit}", ct)
            .ConfigureAwait(false);
        return json is { } j ? ParseBinance(j) : null;
    }

    private static async Task<IReadOnlyList<Candle>?> FromCoinbaseAsync(MarketSymbol s, Spec spec, CancellationToken ct)
    {
        var product = CoinbaseFeed.ToExchangeSymbol(s);
        var end = DateTimeOffset.UtcNow;
        // Coinbase returns at most 300 candles per request.
        var start = end - TimeSpan.FromSeconds(Math.Min(spec.Span.TotalSeconds, spec.CoinbaseSeconds * 299.0));
        var json = await GetJsonAsync(
            $"https://api.exchange.coinbase.com/products/{product}/candles?granularity={spec.CoinbaseSeconds}" +
            $"&start={start.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&end={end.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}", ct)
            .ConfigureAwait(false);
        return json is { } j ? ParseCoinbase(j) : null;
    }

    private static async Task<IReadOnlyList<Candle>?> FromKrakenAsync(MarketSymbol s, Spec spec, CancellationToken ct)
    {
        var since = (DateTimeOffset.UtcNow - spec.Span).ToUnixTimeSeconds();
        var json = await GetJsonAsync(
            $"https://api.kraken.com/0/public/OHLC?pair={s.Base}{s.Quote}&interval={spec.KrakenMinutes}&since={since}", ct)
            .ConfigureAwait(false);
        return json is { } j ? ParseKraken(j) : null;
    }

    private static async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await Http.Client.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
    }

    // [[openTimeMs, open, high, low, close, volume, ...], ...] oldest first
    internal static IReadOnlyList<Candle> ParseBinance(JsonElement json) =>
        json.EnumerateArray()
            .Select(k => new Candle(DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64()),
                Num(k[1]), Num(k[2]), Num(k[3]), Num(k[4]), Num(k[5])))
            .ToList();

    // [[timeSec, low, high, open, close, volume], ...] newest first
    internal static IReadOnlyList<Candle> ParseCoinbase(JsonElement json) =>
        json.EnumerateArray()
            .Select(k => new Candle(DateTimeOffset.FromUnixTimeSeconds(k[0].GetInt64()),
                Num(k[3]), Num(k[2]), Num(k[1]), Num(k[4]), Num(k[5])))
            .OrderBy(c => c.Time)
            .ToList();

    // {"result": {"XXBTZUSD": [[timeSec, open, high, low, close, vwap, volume, count], ...], "last": n}}
    internal static IReadOnlyList<Candle>? ParseKraken(JsonElement json)
    {
        if (!json.TryGetProperty("result", out var result))
            return null;
        foreach (var prop in result.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array)
                continue;
            return prop.Value.EnumerateArray()
                .Select(k => new Candle(DateTimeOffset.FromUnixTimeSeconds(k[0].GetInt64()),
                    Num(k[1]), Num(k[2]), Num(k[3]), Num(k[4]), Num(k[6])))
                .ToList();
        }
        return null;
    }

    private static decimal Num(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDecimal(),
        JsonValueKind.String => decimal.Parse(e.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture),
        _ => throw new FormatException("Not a number"),
    };
}
