using System.Globalization;
using System.Text.Json;
using PricePin.Core.Feeds;
using PricePin.Core.Market;

namespace PricePin.Core.Radar;

public enum RadarKind
{
    /// <summary>A very large market buy.</summary>
    WhaleBuy,

    /// <summary>A very large market sell.</summary>
    WhaleSell,

    /// <summary>A leveraged long position was force-closed (the exchange sold).</summary>
    LongLiquidated,

    /// <summary>A leveraged short position was force-closed (the exchange bought).</summary>
    ShortLiquidated,
}

/// <param name="Symbol">Base coin, e.g. "BTC".</param>
/// <param name="ValueUsd">Size in US dollars (stablecoin pairs count as dollars).</param>
public sealed record RadarEvent(RadarKind Kind, string Symbol, string Exchange, decimal Price, decimal Quantity, decimal ValueUsd, DateTimeOffset Time)
{
    public bool IsWhale => Kind is RadarKind.WhaleBuy or RadarKind.WhaleSell;
}

/// <summary>
/// Watches for whale trades and liquidations as they happen:
/// <list type="bullet">
/// <item>big trades on Binance's spot market (falling back to Coinbase where Binance is blocked),</item>
/// <item>futures liquidations on Binance (every coin) and Bybit (the biggest coins) at the same time.</item>
/// </list>
/// Only events at or above the thresholds are reported. Events fire on a background thread.
/// </summary>
public sealed class RadarService : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _run;

    public event Action<RadarEvent>? EventReceived;

    /// <summary>Which source each half is using right now, e.g. "Binance" / "Bybit", for the window.</summary>
    public event Action<string, string?>? SourceChanged;

    /// <param name="whaleSymbols">Coins to watch for big trades (exchange symbols like "BTC").</param>
    public void Start(IReadOnlyList<string> whaleSymbols, decimal minWhaleUsd, decimal minLiquidationUsd)
    {
        lock (_gate)
        {
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            var previous = _run;
            var symbols = whaleSymbols.Select(s => s.ToUpperInvariant()).Distinct().Take(30).ToList();
            _run = Task.Run(async () =>
            {
                if (previous is not null)
                    await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await Task.WhenAll(
                    symbols.Count == 0 ? Task.CompletedTask : RunWhalesAsync(symbols, minWhaleUsd, cts.Token),
                    RunLiquidationsAsync(minLiquidationUsd, cts.Token)).ConfigureAwait(false);
            });
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts = null;
        }
    }

    // ---- Whale trades ------------------------------------------------------------------

    private Task RunWhalesAsync(IReadOnlyList<string> symbols, decimal min, CancellationToken ct) =>
        RunWithFailoverAsync("whales", ct,
            ("Binance", () => WebSocketSession.RunAsync("Binance trades", BinanceTradesUri(symbols), [],
                span => Emit(ParseBinanceAggTrade(span), min), TimeSpan.FromSeconds(60), TimeSpan.FromHours(23), ct)),
            ("Coinbase", () => WebSocketSession.RunAsync("Coinbase trades", new Uri("wss://ws-feed.exchange.coinbase.com"),
                [CoinbaseSubscribe(symbols.Where(CoinbaseProducts.Contains).DefaultIfEmpty("BTC"))],
                span => Emit(ParseCoinbaseMatch(span), min), TimeSpan.FromSeconds(60), TimeSpan.FromHours(23), ct)));

    // Coinbase drops the whole subscription when one product does not exist, so only ask for
    // coins it is known to list.
    private static readonly HashSet<string> CoinbaseProducts =
        ["BTC", "ETH", "SOL", "XRP", "DOGE", "ADA", "AVAX", "LINK", "LTC", "DOT", "SHIB", "UNI", "BCH", "XLM", "AAVE", "NEAR", "APT", "ARB", "OP", "SUI", "PEPE", "ATOM", "HBAR", "FIL", "ICP", "ETC"];

    internal static Uri BinanceTradesUri(IEnumerable<string> symbols) =>
        new("wss://stream.binance.com:9443/stream?streams=" + string.Join("/", symbols.Select(s => s.ToLowerInvariant() + "usdt@aggTrade")));

    internal static string CoinbaseSubscribe(IEnumerable<string> symbols) =>
        JsonSerializer.Serialize(new { type = "subscribe", product_ids = symbols.Select(s => s + "-USD").ToArray(), channels = new[] { "matches" } });

    // {"stream":"btcusdt@aggTrade","data":{"e":"aggTrade","s":"BTCUSDT","p":"97000.1","q":"2.5","T":1700000000000,"m":true}}
    // m = buyer is the maker, so the aggressive side was a sell.
    internal static RadarEvent? ParseBinanceAggTrade(ReadOnlySpan<byte> utf8)
    {
        using var doc = JsonDocument.Parse(utf8.ToArray());
        var d = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;
        if (Json.String(d, "e") != "aggTrade" || Json.String(d, "s") is not { } s || !s.EndsWith("USDT", StringComparison.Ordinal))
            return null;
        if (Json.Decimal(d, "p") is not { } p || Json.Decimal(d, "q") is not { } q)
            return null;
        var sell = d.TryGetProperty("m", out var m) && m.ValueKind == JsonValueKind.True;
        return new RadarEvent(sell ? RadarKind.WhaleSell : RadarKind.WhaleBuy, s[..^4], "Binance", p, q, p * q,
            Json.UnixMs(d, "T") ?? DateTimeOffset.UtcNow);
    }

    // {"type":"match","product_id":"BTC-USD","size":"1.2","price":"97000","side":"sell","time":"2026-11-15T01:00:00.123Z"}
    // side is the resting (maker) order, so the taker did the opposite.
    internal static RadarEvent? ParseCoinbaseMatch(ReadOnlySpan<byte> utf8)
    {
        using var doc = JsonDocument.Parse(utf8.ToArray());
        var d = doc.RootElement;
        if (Json.String(d, "type") is not ("match" or "last_match") || Json.String(d, "product_id") is not { } product)
            return null;
        if (Json.Decimal(d, "price") is not { } p || Json.Decimal(d, "size") is not { } q)
            return null;
        var dash = product.IndexOf('-');
        if (dash <= 0 || product[(dash + 1)..] is not ("USD" or "USDC" or "USDT"))
            return null;
        var buy = Json.String(d, "side") == "sell";
        return new RadarEvent(buy ? RadarKind.WhaleBuy : RadarKind.WhaleSell, product[..dash], "Coinbase", p, q, p * q,
            Json.Iso(d, "time") ?? DateTimeOffset.UtcNow);
    }

    // ---- Liquidations ------------------------------------------------------------------

    private static readonly string[] BybitSymbols =
        ["BTC", "ETH", "SOL", "XRP", "DOGE", "BNB", "ADA", "AVAX", "LINK", "SUI", "LTC", "DOT", "TRX", "TON", "PEPE", "WIF", "NEAR", "APT", "ARB", "OP"];

    // Both exchanges run at once: they are different markets, so together they show more of
    // what is being wiped out. Binance futures can also connect and stay silent where it is
    // not offered, so Bybit cannot wait for Binance to fail first.
    private Task RunLiquidationsAsync(decimal min, CancellationToken ct) =>
        Task.WhenAll(
            RunWithFailoverAsync("liquidations:Binance", ct,
                ("Binance", () => WebSocketSession.RunAsync("Binance liquidations", new Uri("wss://fstream.binance.com/ws/!forceOrder@arr"), [],
                    span => Emit(ParseBinanceForceOrder(span), min), TimeSpan.FromMinutes(10), TimeSpan.FromHours(23), ct))),
            RunWithFailoverAsync("liquidations:Bybit", ct,
                ("Bybit", () => WebSocketSession.RunAsync("Bybit liquidations", new Uri("wss://stream.bybit.com/v5/public/linear"),
                    [BybitSubscribe(BybitSymbols)],
                    span =>
                    {
                        foreach (var e in ParseBybitLiquidations(span))
                            Emit(e, min);
                    }, TimeSpan.FromMinutes(10), TimeSpan.FromHours(23), ct))));

    internal static string BybitSubscribe(IEnumerable<string> symbols) =>
        JsonSerializer.Serialize(new { op = "subscribe", args = symbols.Select(s => $"allLiquidation.{s}USDT").ToArray() });

    // {"e":"forceOrder","E":1700000000000,"o":{"s":"BTCUSDT","S":"SELL","q":"0.5","p":"95000","ap":"95100","z":"0.5","T":1700000000000}}
    // SELL means a long was closed; BUY means a short was.
    internal static RadarEvent? ParseBinanceForceOrder(ReadOnlySpan<byte> utf8)
    {
        using var doc = JsonDocument.Parse(utf8.ToArray());
        var root = doc.RootElement;
        if (Json.String(root, "e") != "forceOrder" || !root.TryGetProperty("o", out var o))
            return null;
        if (Json.String(o, "s") is not { } s || !s.EndsWith("USDT", StringComparison.Ordinal))
            return null;
        var price = Json.Decimal(o, "ap") is { } ap and > 0 ? ap : Json.Decimal(o, "p") ?? 0;
        var qty = Json.Decimal(o, "z") is { } z and > 0 ? z : Json.Decimal(o, "q") ?? 0;
        if (price <= 0 || qty <= 0)
            return null;
        var kind = Json.String(o, "S") == "SELL" ? RadarKind.LongLiquidated : RadarKind.ShortLiquidated;
        return new RadarEvent(kind, Base(s[..^4]), "Binance", price, qty, price * qty, Json.UnixMs(o, "T") ?? DateTimeOffset.UtcNow);
    }

    // {"topic":"allLiquidation.BTCUSDT","data":[{"T":1700000000000,"s":"BTCUSDT","S":"Buy","v":"0.5","p":"95000"}]}
    // S is the side of the position that was closed: Buy = a long.
    internal static IEnumerable<RadarEvent> ParseBybitLiquidations(ReadOnlySpan<byte> utf8)
    {
        using var doc = JsonDocument.Parse(utf8.ToArray());
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<RadarEvent>();
        foreach (var d in data.EnumerateArray())
        {
            if (Json.String(d, "s") is not { } s || !s.EndsWith("USDT", StringComparison.Ordinal)
                || Json.Decimal(d, "p") is not { } p || Json.Decimal(d, "v") is not { } v)
                continue;
            var kind = Json.String(d, "S") == "Buy" ? RadarKind.LongLiquidated : RadarKind.ShortLiquidated;
            list.Add(new RadarEvent(kind, Base(s[..^4]), "Bybit", p, v, p * v, Json.UnixMs(d, "T") ?? DateTimeOffset.UtcNow));
        }
        return list;
    }

    /// <summary>Futures list small coins in thousands ("1000PEPE"); show the coin itself.</summary>
    private static string Base(string s) =>
        s.StartsWith("1000000", StringComparison.Ordinal) ? s[7..] : s.StartsWith("1000", StringComparison.Ordinal) && s.Length > 4 && char.IsLetter(s[4]) ? s[4..] : s;

    // ---- Plumbing ----------------------------------------------------------------------

    private void Emit(RadarEvent? e, decimal min)
    {
        if (e is not null && e.ValueUsd >= min)
            EventReceived?.Invoke(e);
    }

    /// <summary>
    /// Runs the first source, reconnecting after drops; moves to the next one when a source is
    /// blocked or keeps failing, and comes back to the first after a while.
    /// </summary>
    private async Task RunWithFailoverAsync(string part, CancellationToken ct, params (string Name, Func<Task> Run)[] sources)
    {
        var index = 0;
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var (name, run) = sources[index];
            SourceChanged?.Invoke(part, name);
            var started = DateTime.UtcNow;
            try
            {
                await run().ConfigureAwait(false);
                return; // cancelled
            }
            catch (FeedException ex)
            {
                // A session that ran a while and then dropped is a normal reconnect.
                failures = DateTime.UtcNow - started > TimeSpan.FromMinutes(2) ? 1 : failures + 1;
                if (ex.Failure == FeedFailure.Blocked && sources.Length == 1)
                {
                    // Nothing to fall back to: try again much later in case the network changes.
                    SourceChanged?.Invoke(part, null);
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(30), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    failures = 0;
                    continue;
                }
                if (ex.Failure == FeedFailure.Blocked || failures >= 3)
                {
                    index = (index + 1) % sources.Length;
                    failures = 0;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                failures++;
            }
            SourceChanged?.Invoke(part, null);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * Math.Pow(2, failures))), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? run;
        lock (_gate)
        {
            _cts?.Cancel();
            run = _run;
        }
        if (run is not null)
            await run.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    internal static string FormatUsd(decimal v) => "$" + (v >= 1_000_000m
        ? (v / 1_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + "M"
        : (v / 1_000m).ToString("0", CultureInfo.InvariantCulture) + "K");
}
