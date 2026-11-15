using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Coins;
using PricePin.Core.Dex;
using PricePin.Core.Feeds;
using PricePin.Core.Market;

namespace PricePin.Core.History;

/// <summary>
/// Fetches recent closes for the sparklines: the last hour, day or week. Tries Binance, then
/// Coinbase, then Kraken, so it works wherever the user is. DEX tokens come from GeckoTerminal.
/// </summary>
public sealed class PriceHistoryService
{
    public const int Points = 48;

    private sealed record Spec(string Binance, int BinanceLimit, int CoinbaseSeconds, int CoinbasePoints,
        int KrakenMinutes, TimeSpan Span, string GeckoFrame, int GeckoAggregate, int GeckoLimit);

    private static readonly Dictionary<ChangePeriod, Spec> Specs = new()
    {
        [ChangePeriod.Hour] = new("1m", 61, 60, 61, 1, TimeSpan.FromHours(1), "minute", 1, 61),
        [ChangePeriod.Day] = new("30m", Points, 900, 96, 30, TimeSpan.FromDays(1), "minute", 15, 96),
        [ChangePeriod.Week] = new("4h", 42, 21600, 28, 240, TimeSpan.FromDays(7), "hour", 4, 42),
    };

    private readonly FiatRates _rates;

    public PriceHistoryService(FiatRates? rates = null) => _rates = rates ?? FiatRates.Shared;

    /// <summary>Returns oldest-to-newest closes, or an empty list if no source has the coin.</summary>
    public Task<IReadOnlyList<decimal>> GetLast24hAsync(MarketSymbol symbol, CancellationToken ct = default) =>
        GetAsync(symbol, ChangePeriod.Day, ct);

    /// <summary>Oldest-to-newest closes covering <paramref name="period"/>, or empty if no source has the coin.</summary>
    public async Task<IReadOnlyList<decimal>> GetAsync(MarketSymbol symbol, ChangePeriod period, CancellationToken ct = default)
    {
        var spec = Specs[period];
        Func<Task<IReadOnlyList<decimal>?>>[] sources = CoinKey.IsDex(symbol.Base)
            ? [() => FromGeckoTerminalAsync(symbol, spec, ct)]
            : [() => FromBinanceAsync(symbol, spec, ct), () => FromCoinbaseAsync(symbol, spec, ct), () => FromKrakenAsync(symbol, spec, ct)];
        foreach (var source in sources)
        {
            try
            {
                if (await source().ConfigureAwait(false) is { Count: > 1 } closes)
                    return closes;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                           or FormatException or IndexOutOfRangeException
                                       && !ct.IsCancellationRequested)
            {
                // Try the next source.
            }
        }
        return [];
    }

    private async Task<IReadOnlyList<decimal>?> FromGeckoTerminalAsync(MarketSymbol s, Spec spec, CancellationToken ct)
    {
        if (CoinCatalog.GetToken(s.Base) is not { PairAddress: { } pool } token)
            return null;
        if (await _rates.UsdToAsync(s.Quote, ct).ConfigureAwait(false) is not { } rate)
            return null;
        var candles = await GeckoTerminalClient.GetCandlesAsync(token.Chain, pool, spec.GeckoFrame, spec.GeckoAggregate, spec.GeckoLimit, ct)
            .ConfigureAwait(false);
        return candles?.Select(c => c.Close * rate).ToList();
    }

    private static async Task<IReadOnlyList<decimal>?> FromBinanceAsync(MarketSymbol s, Spec spec, CancellationToken ct)
    {
        var symbol = BinanceFeed.ToExchangeSymbol(s).ToUpperInvariant();
        var json = await GetJsonAsync($"https://data-api.binance.vision/api/v3/klines?symbol={symbol}&interval={spec.Binance}&limit={spec.BinanceLimit}", ct)
            .ConfigureAwait(false);
        return json is { } j ? ParseBinanceKlines(j, spec.BinanceLimit) : null;
    }

    private static async Task<IReadOnlyList<decimal>?> FromCoinbaseAsync(MarketSymbol s, Spec spec, CancellationToken ct)
    {
        var product = CoinbaseFeed.ToExchangeSymbol(s);
        var json = await GetJsonAsync($"https://api.exchange.coinbase.com/products/{product}/candles?granularity={spec.CoinbaseSeconds}", ct)
            .ConfigureAwait(false);
        return json is { } j ? ParseCoinbaseCandles(j, spec.CoinbasePoints) : null;
    }

    private static async Task<IReadOnlyList<decimal>?> FromKrakenAsync(MarketSymbol s, Spec spec, CancellationToken ct)
    {
        var since = (DateTimeOffset.UtcNow - spec.Span).ToUnixTimeSeconds();
        var json = await GetJsonAsync($"https://api.kraken.com/0/public/OHLC?pair={s.Base}{s.Quote}&interval={spec.KrakenMinutes}&since={since}", ct)
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
    internal static IReadOnlyList<decimal> ParseBinanceKlines(JsonElement json, int max = Points) =>
        json.EnumerateArray()
            .Select(k => ParseNumber(k[4]))
            .OfType<decimal>()
            .TakeLast(max)
            .ToList();

    // [[time, low, high, open, close, volume], ...] newest first
    internal static IReadOnlyList<decimal> ParseCoinbaseCandles(JsonElement json, int max = Points) =>
        json.EnumerateArray()
            .Take(max)
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
