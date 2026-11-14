using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Market;

namespace PricePin.Core.Feeds;

/// <summary>
/// Binance's public market-data REST mirror (data-api.binance.vision).
/// One request covers every coin; if Binance rejects the batch because a coin
/// isn't listed, coins are fetched one by one and the unknown ones skipped from then on.
/// </summary>
public sealed class BinanceRestFeed : RestPollingFeedBase
{
    private readonly HashSet<string> _unknown = new(StringComparer.OrdinalIgnoreCase);

    public override string Name => "Binance (REST)";

    public Uri BaseUri { get; init; } = new("https://data-api.binance.vision/");

    protected override async Task<IReadOnlyList<Ticker>> FetchAsync(IReadOnlyList<MarketSymbol> symbols, CancellationToken ct)
    {
        var map = symbols
            .Select(s => (Symbol: BinanceFeed.ToExchangeSymbol(s).ToUpperInvariant(), s.Base))
            .Where(x => !_unknown.Contains(x.Symbol))
            .ToDictionary(x => x.Symbol, x => x.Base);
        if (map.Count == 0)
            return [];

        try
        {
            return await FetchBatchAsync(map, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            var result = new List<Ticker>();
            foreach (var (symbol, @base) in map)
            {
                try
                {
                    result.AddRange(await FetchBatchAsync(new() { [symbol] = @base }, ct).ConfigureAwait(false));
                }
                catch (HttpRequestException one) when (one.StatusCode == HttpStatusCode.BadRequest)
                {
                    _unknown.Add(symbol);
                }
            }
            return result;
        }
    }

    private async Task<IReadOnlyList<Ticker>> FetchBatchAsync(Dictionary<string, string> map, CancellationToken ct)
    {
        var symbolsParam = Uri.EscapeDataString(JsonSerializer.Serialize(map.Keys));
        var uri = new Uri(BaseUri, $"api/v3/ticker/24hr?symbols={symbolsParam}");
        using var response = await Http.Client.GetAsync(uri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
        return Parse(json, map, Name);
    }

    internal static IReadOnlyList<Ticker> Parse(JsonElement array, IReadOnlyDictionary<string, string> baseBySymbol, string source)
    {
        var list = new List<Ticker>();
        foreach (var t in array.EnumerateArray())
        {
            if (Json.String(t, "symbol") is not { } symbol || !baseBySymbol.TryGetValue(symbol, out var @base)
                || Json.Decimal(t, "lastPrice") is not { } last)
                continue;

            list.Add(new Ticker
            {
                Base = @base,
                Last = last,
                Open24h = Json.Decimal(t, "openPrice"),
                High24h = Json.Decimal(t, "highPrice"),
                Low24h = Json.Decimal(t, "lowPrice"),
                Volume24h = Json.Decimal(t, "volume"),
                ChangePercent24h = Json.Decimal(t, "priceChangePercent"),
                ExchangeTime = Json.UnixMs(t, "closeTime"),
                Source = source,
            });
        }
        return list;
    }
}
