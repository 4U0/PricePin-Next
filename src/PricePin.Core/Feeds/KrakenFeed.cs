using System.Text.Json;
using PricePin.Core.Market;

namespace PricePin.Core.Feeds;

/// <summary>
/// Kraken WebSocket v2. The <c>ticker</c> channel updates on every trade by default
/// and carries 24h stats. Kraken also sends a heartbeat every second when quiet.
/// </summary>
public sealed class KrakenFeed : WebSocketFeedBase
{
    private Dictionary<string, string> _baseBySymbol = new(StringComparer.OrdinalIgnoreCase);

    public override string Name => "Kraken";

    public Uri Endpoint { get; init; } = new("wss://ws.kraken.com/v2");

    public static string ToExchangeSymbol(MarketSymbol s) => $"{s.Base}/{s.Quote}";

    protected override Uri BuildUri(IReadOnlyList<MarketSymbol> symbols)
    {
        _baseBySymbol = symbols.ToDictionary(ToExchangeSymbol, s => s.Base, StringComparer.OrdinalIgnoreCase);
        return Endpoint;
    }

    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyList<MarketSymbol> symbols)
    {
        // One message per symbol: Kraken rejects a whole batch if any symbol is unknown.
        var id = 1;
        foreach (var s in symbols)
        {
            yield return JsonSerializer.Serialize(new
            {
                method = "subscribe",
                @params = new { channel = "ticker", symbol = new[] { ToExchangeSymbol(s) } },
                req_id = id++,
            });
        }
    }

    protected internal override void ParseMessage(ReadOnlySpan<byte> utf8Json, Action<Ticker> emit)
    {
        using var doc = JsonDocument.Parse(utf8Json.ToArray());
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Json.String(root, "channel") != "ticker" || !root.TryGetProperty("data", out var data))
            return;

        foreach (var t in data.EnumerateArray())
        {
            var symbol = Json.String(t, "symbol");
            if (symbol is null || !_baseBySymbol.TryGetValue(symbol, out var @base) || Json.Decimal(t, "last") is not { } last)
                continue;

            var change = Json.Decimal(t, "change");
            emit(new Ticker
            {
                Base = @base,
                Last = last,
                Open24h = change is { } c ? last - c : null,
                High24h = Json.Decimal(t, "high"),
                Low24h = Json.Decimal(t, "low"),
                Volume24h = Json.Decimal(t, "volume"),
                ChangePercent24h = Json.Decimal(t, "change_pct"),
                Source = Name,
            });
        }
    }

    internal void PrepareForTest(IReadOnlyList<MarketSymbol> symbols) => BuildUri(symbols);
}
