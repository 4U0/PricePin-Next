using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Feeds;
using PricePin.Core.Market;

namespace PricePin.Core.History;

/// <summary>
/// Fetches the last 24 hours of prices (48 half-hour closes) for the sparklines.
/// Tries Binance, then Coinbase, then Kraken, so it works wherever the user is.
/// </summary>
public sealed class PriceHistoryService
{
    public const int Points = 48;

    private readonly Func<MarketSymbol, CancellationToken, Task<IReadOnlyList<decimal>?>>[] _sources;

    public PriceHistoryService()
    {
        _sources = [FromBinanceAsync, FromCoinbaseAsync, FromKrakenAsync];
    }

    /// <summary>Returns oldest-to-newest closes, or an empty list if no source has the coin.</summary>
    public async Task<IReadOnlyList<decimal>> GetLast24hAsync(MarketSymbol symbol, CancellationToken ct = default)
    {
        foreach (var source in _sources)
        {
            try
            {
                if (await source(symbol, ct).ConfigureAwait(false) is { Count: > 1 } closes)
                    return closes;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                       && !ct.IsCancellationRequested)
            {
                // Try the next source.
            }
        }
        return [];
    }

    private static async Task<IReadOnlyList<decimal>?> FromBinanceAsync(MarketSymbol s, CancellationToken ct)
    {
        var symbol = BinanceFeed.ToExchangeSymbol(s).ToUpperInvariant();
        var json = await GetJsonAsync($"https://data-api.binance.vision/api/v3/klines?symbol={symbol}&interval=30m&limit={Points}", ct)
            .ConfigureAwait(false);
        return json is { } j ? ParseBinanceKlines(j) : null;
    }

    private static async Task<IReadOnlyList<decimal>?> FromCoinbaseAsync(MarketSymbol s, CancellationToken ct)
    {
        var product = CoinbaseFeed.ToExchangeSymbol(s);
        var json = await GetJsonAsync($"https://api.exchange.coinbase.com/products/{product}/candles?granularity=1800", ct)
            .ConfigureAwait(false);
        return json is { } j ? ParseCoinbaseCandles(j) : null;
    }

    private static async Task<IReadOnlyList<decimal>?> FromKrakenAsync(MarketSymbol s, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds();
        var json = await GetJsonAsync($"https://api.kraken.com/0/public/OHLC?pair={s.Base}{s.Quote}&interval=30&since={since}", ct)
            .ConfigureAwait(false);
        return json is { } j ? ParseKrakenOhlc(j) : null;
    }

    private static async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await Http.Client.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
    }

    // [[openTime, open, high, low, close, volume, closeTime, ...], ...] oldest first
    internal static IReadOnlyList<decimal> ParseBinanceKlines(JsonElement json) =>
        json.EnumerateArray()
            .Select(k => ParseNumber(k[4]))
            .OfType<decimal>()
            .TakeLast(Points)
            .ToList();

    // [[time, low, high, open, close, volume], ...] newest first
    internal static IReadOnlyList<decimal> ParseCoinbaseCandles(JsonElement json) =>
        json.EnumerateArray()
            .Take(Points)
            .Select(k => ParseNumber(k[4]))
            .OfType<decimal>()
            .Reverse()
            .ToList();

    // {"error":[], "result": {"XXBTZUSD": [[time, open, high, low, close, vwap, volume, count], ...], "last": n}}
    internal static IReadOnlyList<decimal>? ParseKrakenOhlc(JsonElement json)
    {
        if (!json.TryGetProperty("result", out var result))
            return null;
        foreach (var prop in result.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                return prop.Value.EnumerateArray()
                    .Select(k => ParseNumber(k[4]))
                    .OfType<decimal>()
                    .TakeLast(Points)
                    .ToList();
            }
        }
        return null;
    }

    private static decimal? ParseNumber(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDecimal(),
        JsonValueKind.String => decimal.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };
}
