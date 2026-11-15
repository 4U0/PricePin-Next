using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Feeds;

namespace PricePin.Core.Market;

/// <summary>
/// US dollar exchange rates, for prices that only come in dollars (DEX tokens) when the user
/// shows prices in another currency. Coinbase's public rates first, then open.er-api.com.
/// Cached for 30 minutes; a stale rate is kept if neither source answers.
/// </summary>
public sealed class FiatRates
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    public static FiatRates Shared { get; } = new();

    private readonly Dictionary<string, decimal> _rates = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private DateTime _fetchedAt;

    /// <summary>Dollar-pegged quotes need no conversion.</summary>
    public static bool IsDollar(string quote) => quote.ToUpperInvariant() is "USD" or "USDT" or "USDC";

    /// <summary>How many units of <paramref name="quote"/> one US dollar buys, or null if unknown.</summary>
    public async Task<decimal?> UsdToAsync(string quote, CancellationToken ct = default)
    {
        if (IsDollar(quote))
            return 1m;
        if (DateTime.UtcNow - _fetchedAt > CacheFor)
            await RefreshAsync(ct).ConfigureAwait(false);
        lock (_rates)
            return _rates.TryGetValue(quote, out var r) ? r : null;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        await _refresh.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow - _fetchedAt <= CacheFor)
                return;
            foreach (var (url, parse) in new (string, Func<JsonElement, Dictionary<string, decimal>?>)[]
                     {
                         ("https://api.coinbase.com/v2/exchange-rates?currency=USD", ParseCoinbase),
                         ("https://open.er-api.com/v6/latest/USD", ParseOpenEr),
                     })
            {
                try
                {
                    using var response = await Http.Client.GetAsync(url, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        continue;
                    var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
                    if (parse(json) is { Count: > 0 } rates)
                    {
                        lock (_rates)
                        {
                            foreach (var (k, v) in rates)
                                _rates[k] = v;
                        }
                        _fetchedAt = DateTime.UtcNow;
                        return;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
                {
                    // Try the next source.
                }
            }
            // Neither answered: try again in a few minutes rather than on every call.
            _fetchedAt = DateTime.UtcNow - CacheFor + TimeSpan.FromMinutes(2);
        }
        finally
        {
            _refresh.Release();
        }
    }

    // {"data":{"currency":"USD","rates":{"EUR":"0.92",...}}}
    internal static Dictionary<string, decimal>? ParseCoinbase(JsonElement json) =>
        json.TryGetProperty("data", out var data) && data.TryGetProperty("rates", out var rates) ? ParseRates(rates) : null;

    // {"result":"success","rates":{"EUR":0.92,...}}
    internal static Dictionary<string, decimal>? ParseOpenEr(JsonElement json) =>
        json.TryGetProperty("rates", out var rates) ? ParseRates(rates) : null;

    private static Dictionary<string, decimal> ParseRates(JsonElement rates)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (rates.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var prop in rates.EnumerateObject())
        {
            if (Json.Decimal(rates, prop.Name) is > 0 and var rate)
                result[prop.Name] = rate;
        }
        return result;
    }
}
