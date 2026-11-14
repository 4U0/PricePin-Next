using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Market;

namespace PricePin.Core.Feeds;

/// <summary>Coinbase Exchange public REST: one <c>/stats</c> request per coin, run in parallel.</summary>
public sealed class CoinbaseRestFeed : RestPollingFeedBase
{
    private readonly HashSet<string> _unknown = new(StringComparer.OrdinalIgnoreCase);

    public override string Name => "Coinbase (REST)";

    public Uri BaseUri { get; init; } = new("https://api.exchange.coinbase.com/");

    protected override async Task<IReadOnlyList<Ticker>> FetchAsync(IReadOnlyList<MarketSymbol> symbols, CancellationToken ct)
    {
        _lastError = null;
        var tasks = symbols
            .Where(s => !_unknown.Contains(CoinbaseFeed.ToExchangeSymbol(s)))
            .Select(s => FetchOneAsync(s, ct));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var tickers = results.OfType<Ticker>().ToList();

        // If every request failed, surface it so the service can count the failure.
        if (tickers.Count == 0 && results.Length > 0 && _lastError is { } error)
            throw error;
        return tickers;
    }

    private HttpRequestException? _lastError;

    private async Task<Ticker?> FetchOneAsync(MarketSymbol symbol, CancellationToken ct)
    {
        var product = CoinbaseFeed.ToExchangeSymbol(symbol);
        try
        {
            using var response = await Http.Client.GetAsync(new Uri(BaseUri, $"products/{product}/stats"), ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _unknown.Add(product);
                return null;
            }
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
            return Parse(json, symbol.Base, Name);
        }
        catch (HttpRequestException ex)
        {
            _lastError = ex;
            return null;
        }
    }

    internal static Ticker? Parse(JsonElement stats, string @base, string source)
    {
        if (Json.Decimal(stats, "last") is not { } last)
            return null;
        var open = Json.Decimal(stats, "open");
        return new Ticker
        {
            Base = @base,
            Last = last,
            Open24h = open,
            High24h = Json.Decimal(stats, "high"),
            Low24h = Json.Decimal(stats, "low"),
            Volume24h = Json.Decimal(stats, "volume"),
            ChangePercent24h = open is > 0 ? (last - open.Value) / open.Value * 100m : null,
            Source = source,
        };
    }
}
