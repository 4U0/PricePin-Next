using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Feeds;

namespace PricePin.Core.Coins;

/// <param name="Symbol">e.g. "SOL".</param>
/// <param name="Name">e.g. "Solana", or the symbol if no name is known.</param>
/// <param name="Exchanges">Exchanges that trade it against the chosen currency.</param>
public sealed record CatalogCoin(string Symbol, string Name, IReadOnlyList<string> Exchanges);

/// <summary>
/// Every coin that can be shown for a given currency, gathered from Binance, Coinbase
/// and Kraken's public market lists. Cached on disk for a day; a stale cache is used
/// if the exchanges can't be reached.
/// </summary>
public sealed class CoinCatalogService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions CacheJson = new() { WriteIndented = false };

    private readonly string _cacheDir;

    public CoinCatalogService(string cacheDirectory)
    {
        _cacheDir = cacheDirectory;
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<IReadOnlyList<CatalogCoin>> GetCoinsAsync(string quote, CancellationToken ct = default)
    {
        quote = quote.ToUpperInvariant();
        var cachePath = Path.Combine(_cacheDir, $"catalog-{quote}.json");

        if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < CacheLifetime
            && await ReadCacheAsync(cachePath, ct).ConfigureAwait(false) is { Count: > 0 } fresh)
        {
            return fresh;
        }

        var binance = Try(() => FetchBinanceAsync(quote, ct));
        var coinbase = Try(() => FetchCoinbaseAsync(quote, ct));
        var kraken = Try(() => FetchKrakenAsync(quote, ct));
        var names = Try(() => FetchCoinbaseNamesAsync(ct));
        await Task.WhenAll(binance, coinbase, kraken, names).ConfigureAwait(false);

        var coins = Merge(
            [("Binance", binance.Result), ("Coinbase", coinbase.Result), ("Kraken", kraken.Result)],
            names.Result ?? new Dictionary<string, string>());

        if (coins.Count > 0)
        {
            await WriteCacheAsync(cachePath, coins, ct).ConfigureAwait(false);
            return coins;
        }

        // Offline: an old list is better than none.
        return await ReadCacheAsync(cachePath, ct).ConfigureAwait(false) ?? [];
    }

    /// <summary>Filters and ranks coins for a search box. Empty query returns the most popular.</summary>
    public static IReadOnlyList<CatalogCoin> Search(IReadOnlyList<CatalogCoin> coins, string? query, int max = 60)
    {
        query = query?.Trim();
        if (string.IsNullOrEmpty(query))
            return coins.Take(max).ToList();

        return coins
            .Select(c => (Coin: c, Score: Score(c, query)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => CoinCatalog.PopularityRank(x.Coin.Symbol))
            .ThenByDescending(x => x.Coin.Exchanges.Count)
            .Select(x => x.Coin)
            .Take(max)
            .ToList();

        static int Score(CatalogCoin c, string q)
        {
            if (c.Symbol.Equals(q, StringComparison.OrdinalIgnoreCase)) return 100;
            if (c.Name.Equals(q, StringComparison.OrdinalIgnoreCase)) return 90;
            if (c.Symbol.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 70;
            if (c.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 60;
            if (c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 30;
            return 0;
        }
    }

    internal static IReadOnlyList<CatalogCoin> Merge(
        IEnumerable<(string Exchange, IReadOnlyCollection<string>? Bases)> sources,
        IReadOnlyDictionary<string, string> names)
    {
        var byBase = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (exchange, bases) in sources)
        {
            foreach (var b in bases ?? [])
            {
                if (!byBase.TryGetValue(b, out var list))
                    byBase[b] = list = [];
                list.Add(exchange);
            }
        }

        return byBase
            .Select(kv =>
            {
                var symbol = kv.Key.ToUpperInvariant();
                var name = CoinCatalog.IsKnown(symbol) ? CoinCatalog.Get(symbol).Name
                    : names.TryGetValue(symbol, out var n) ? n
                    : symbol;
                return new CatalogCoin(symbol, name, kv.Value);
            })
            .OrderBy(c => CoinCatalog.PopularityRank(c.Symbol))
            .ThenByDescending(c => c.Exchanges.Count)
            .ThenBy(c => c.Symbol, StringComparer.Ordinal)
            .ToList();
    }

    // ---- Sources -----------------------------------------------------------------------

    private static async Task<IReadOnlyCollection<string>?> FetchBinanceAsync(string quote, CancellationToken ct)
    {
        var json = await Http.Client.GetFromJsonAsync<JsonElement>(
            "https://data-api.binance.vision/api/v3/exchangeInfo?permissions=SPOT&symbolStatus=TRADING", ct).ConfigureAwait(false);
        return ParseBinance(json, quote == "USD" ? "USDT" : quote);
    }

    private static async Task<IReadOnlyCollection<string>?> FetchCoinbaseAsync(string quote, CancellationToken ct)
    {
        var json = await Http.Client.GetFromJsonAsync<JsonElement>("https://api.exchange.coinbase.com/products", ct).ConfigureAwait(false);
        return ParseCoinbase(json, quote);
    }

    private static async Task<IReadOnlyCollection<string>?> FetchKrakenAsync(string quote, CancellationToken ct)
    {
        var json = await Http.Client.GetFromJsonAsync<JsonElement>("https://api.kraken.com/0/public/AssetPairs", ct).ConfigureAwait(false);
        return ParseKraken(json, quote);
    }

    private static async Task<IReadOnlyDictionary<string, string>?> FetchCoinbaseNamesAsync(CancellationToken ct)
    {
        var json = await Http.Client.GetFromJsonAsync<JsonElement>("https://api.exchange.coinbase.com/currencies", ct).ConfigureAwait(false);
        return ParseCoinbaseNames(json);
    }

    internal static IReadOnlyCollection<string> ParseBinance(JsonElement json, string quote) =>
        json.GetProperty("symbols").EnumerateArray()
            .Where(s => Json.String(s, "quoteAsset") == quote && Json.String(s, "status") is null or "TRADING")
            .Select(s => Json.String(s, "baseAsset"))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyCollection<string> ParseCoinbase(JsonElement json, string quote) =>
        json.EnumerateArray()
            .Where(p => Json.String(p, "quote_currency") == quote
                        && Json.String(p, "status") is null or "online"
                        && !(p.TryGetProperty("trading_disabled", out var d) && d.ValueKind == JsonValueKind.True))
            .Select(p => Json.String(p, "base_currency"))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyCollection<string> ParseKraken(JsonElement json, string quote)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!json.TryGetProperty("result", out var pairs))
            return result;
        foreach (var pair in pairs.EnumerateObject())
        {
            if (Json.String(pair.Value, "wsname") is not { } wsname || Json.String(pair.Value, "status") is not (null or "online"))
                continue;
            var parts = wsname.Split('/');
            if (parts.Length == 2 && KrakenAlias(parts[1]) == quote)
                result.Add(KrakenAlias(parts[0]));
        }
        return result;
    }

    /// <summary>Kraken's REST lists use legacy names (XBT for BTC, XDG for DOGE); its WebSocket v2 uses the common ones.</summary>
    private static string KrakenAlias(string s) => s switch
    {
        "XBT" => "BTC",
        "XDG" => "DOGE",
        _ => s,
    };

    internal static IReadOnlyDictionary<string, string> ParseCoinbaseNames(JsonElement json)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in json.EnumerateArray())
        {
            if (Json.String(c, "id") is { } id && Json.String(c, "name") is { Length: > 0 } name)
                names[id] = name;
        }
        return names;
    }

    private static async Task<T?> Try<T>(Func<Task<T?>> fetch) where T : class
    {
        try
        {
            return await fetch().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<CatalogCoin>?> ReadCacheAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<CatalogCoin>>(stream, CacheJson, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task WriteCacheAsync(string path, IReadOnlyList<CatalogCoin> coins, CancellationToken ct)
    {
        try
        {
            var tmp = path + ".tmp";
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, coins, CacheJson, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The list still works this session.
        }
    }
}
