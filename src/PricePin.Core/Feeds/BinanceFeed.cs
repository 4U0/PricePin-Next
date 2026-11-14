using System.Text.Json;
using PricePin.Core.Market;

namespace PricePin.Core.Feeds;

/// <summary>
/// Binance spot streams. Uses two streams per coin: <c>@aggTrade</c> for the price of
/// every trade (instant) and <c>@ticker</c> for 24h stats (once a second).
/// Binance has no USD pairs, so USD is served by USDT pairs.
/// </summary>
public sealed class BinanceFeed : WebSocketFeedBase
{
    private const int MaxParamsPerMessage = 200;
    private Dictionary<string, string> _baseBySymbol = new(StringComparer.OrdinalIgnoreCase);

    public override string Name => "Binance";

    public Uri Endpoint { get; init; } = new("wss://stream.binance.com:9443/stream");

    public static string ToExchangeSymbol(MarketSymbol s) =>
        (s.Base + (s.Quote == "USD" ? "USDT" : s.Quote)).ToLowerInvariant();

    protected override Uri BuildUri(IReadOnlyList<MarketSymbol> symbols)
    {
        _baseBySymbol = symbols.ToDictionary(ToExchangeSymbol, s => s.Base, StringComparer.OrdinalIgnoreCase);
        return Endpoint;
    }

    protected override IEnumerable<string> BuildSubscribeMessages(IReadOnlyList<MarketSymbol> symbols)
    {
        var streams = symbols
            .Select(ToExchangeSymbol)
            .SelectMany(s => new[] { s + "@ticker", s + "@aggTrade" })
            .ToList();

        var id = 1;
        foreach (var chunk in streams.Chunk(MaxParamsPerMessage))
            yield return JsonSerializer.Serialize(new { method = "SUBSCRIBE", @params = chunk, id = id++ });
    }

    protected internal override void ParseMessage(ReadOnlySpan<byte> utf8Json, Action<Ticker> emit)
    {
        using var doc = JsonDocument.Parse(utf8Json.ToArray());
        var root = doc.RootElement;

        // Combined-stream envelope: {"stream": "...", "data": {...}}. Subscription acks have no "data".
        if (!root.TryGetProperty("data", out var data))
            return;

        var symbol = Json.String(data, "s");
        if (symbol is null || !_baseBySymbol.TryGetValue(symbol, out var @base))
            return;

        switch (Json.String(data, "e"))
        {
            case "aggTrade" when Json.Decimal(data, "p") is { } price:
                emit(new Ticker
                {
                    Base = @base,
                    Last = price,
                    ExchangeTime = Json.UnixMs(data, "T"),
                    Source = Name,
                });
                break;

            case "24hrTicker" when Json.Decimal(data, "c") is { } last:
                emit(new Ticker
                {
                    Base = @base,
                    Last = last,
                    Open24h = Json.Decimal(data, "o"),
                    High24h = Json.Decimal(data, "h"),
                    Low24h = Json.Decimal(data, "l"),
                    Volume24h = Json.Decimal(data, "v"),
                    ChangePercent24h = Json.Decimal(data, "P"),
                    ExchangeTime = Json.UnixMs(data, "E"),
                    Source = Name,
                });
                break;
        }
    }

    /// <summary>For tests: prepares the symbol map without connecting.</summary>
    internal void PrepareForTest(IReadOnlyList<MarketSymbol> symbols) => BuildUri(symbols);
}
