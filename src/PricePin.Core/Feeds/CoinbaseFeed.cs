using System.Text.Json;
using PricePin.Core.Market;

namespace PricePin.Core.Feeds;

/// <summary>
/// Coinbase Advanced Trade public market data. The <c>ticker</c> channel pushes on
/// every trade and includes 24h stats; <c>heartbeats</c> keeps quiet connections open.
/// Each product is subscribed separately so one unknown coin can't break the rest.
/// </summary>
public sealed class CoinbaseFeed : WebSocketFeedBase
{
    private Dictionary<string, string> _baseByProduct = new(StringComparer.OrdinalIgnoreCase);

    public override string Name => "Coinbase";

    public Uri Endpoint { get; init; } = new("wss://advanced-trade-ws.coinbase.com");

    public static string ToExchangeSymbol(MarketSymbol s) => $"{s.Base}-{s.Quote}";

    protected override Uri BuildUri(IReadOnlyList<MarketSymbol> symbols)
    {
        _baseByProduct = symbols.ToDictionary(ToExchangeSymbol, s => s.Base, StringComparer.OrdinalIgnoreCase);
        return Endpoint;
    }

    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyList<MarketSymbol> symbols)
    {
        yield return JsonSerializer.Serialize(new { type = "subscribe", channel = "heartbeats" });
        foreach (var s in symbols)
        {
            yield return JsonSerializer.Serialize(new
            {
                type = "subscribe",
                channel = "ticker",
                product_ids = new[] { ToExchangeSymbol(s) },
            });
        }
    }

    protected internal override void ParseMessage(ReadOnlySpan<byte> utf8Json, Action<Ticker> emit)
    {
        using var doc = JsonDocument.Parse(utf8Json.ToArray());
        var root = doc.RootElement;
        if (Json.String(root, "channel") != "ticker" || !root.TryGetProperty("events", out var events))
            return;

        var time = Json.Iso(root, "timestamp");
        foreach (var ev in events.EnumerateArray())
        {
            if (!ev.TryGetProperty("tickers", out var tickers))
                continue;

            foreach (var t in tickers.EnumerateArray())
            {
                var product = Json.String(t, "product_id");
                if (product is null || !_baseByProduct.TryGetValue(product, out var @base) || Json.Decimal(t, "price") is not { } price)
                    continue;

                var pct = Json.Decimal(t, "price_percent_chg_24_h");
                emit(new Ticker
                {
                    Base = @base,
                    Last = price,
                    // Coinbase gives the % change, not the open; work the open back out from it.
                    Open24h = pct is { } p && p != -100m ? price / (1 + p / 100m) : null,
                    High24h = Json.Decimal(t, "high_24_h"),
                    Low24h = Json.Decimal(t, "low_24_h"),
                    Volume24h = Json.Decimal(t, "volume_24_h"),
                    ChangePercent24h = pct,
                    ExchangeTime = time,
                    Source = Name,
                });
            }
        }
    }

    internal void PrepareForTest(IReadOnlyList<MarketSymbol> symbols) => BuildUri(symbols);
}
