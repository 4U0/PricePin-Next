using System.Text.Json;
using PricePin.Core.Feeds;

namespace PricePin.Core.Listings;

/// <summary>A coin an exchange started trading since the last check.</summary>
public sealed record NewListing(string Exchange, string Symbol);

/// <summary>
/// Notices new coins on Binance, Coinbase, Kraken and Bybit by comparing each exchange's public market
/// list with the coins seen before. The coins seen are kept in a small file, so a restart doesn't
/// announce the whole list again. The first time an exchange answers, its list is only remembered.
/// </summary>
public sealed class ListingService
{
    /// <summary>More new coins than this at once means the exchange changed its list format, not a listing.</summary>
    public const int MaxNewAtOnce = 15;

    private static readonly JsonSerializerOptions FileJson = new() { WriteIndented = false };

    private readonly string _path;
    private Dictionary<string, HashSet<string>>? _known;

    public ListingService(string cacheDirectory)
    {
        Directory.CreateDirectory(cacheDirectory);
        _path = Path.Combine(cacheDirectory, "listings.json");
    }

    public static IReadOnlyList<(string Name, Func<CancellationToken, Task<IReadOnlyCollection<string>?>> Fetch)> Exchanges { get; } =
    [
        ("Binance", FetchBinanceAsync),
        ("Coinbase", FetchCoinbaseAsync),
        ("Kraken", FetchKrakenAsync),
        ("Bybit", FetchBybitAsync),
    ];

    /// <summary>New coins since the last check. Exchanges that can't be reached are skipped this time.</summary>
    public async Task<IReadOnlyList<NewListing>> CheckAsync(CancellationToken ct = default)
    {
        _known ??= Load();
        var tasks = Exchanges.Select(e => TryFetchAsync(e.Fetch, ct)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var found = new List<NewListing>();
        var changed = false;
        for (var i = 0; i < Exchanges.Count; i++)
        {
            if (tasks[i].Result is not { Count: > 0 } current)
                continue;
            var before = _known.TryGetValue(Exchanges[i].Name, out var set) ? set.Count : -1;
            found.AddRange(Diff(_known, Exchanges[i].Name, current));
            changed |= _known[Exchanges[i].Name].Count != before;
        }
        if (changed)
            Save(_known);
        return found;
    }

    /// <summary>
    /// Compares one exchange's coins with the ones seen before and remembers the new ones.
    /// Delisted coins stay remembered, so a coin that comes back isn't announced as new.
    /// </summary>
    internal static List<NewListing> Diff(Dictionary<string, HashSet<string>> known, string exchange, IReadOnlyCollection<string> current)
    {
        if (!known.TryGetValue(exchange, out var seen))
        {
            known[exchange] = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
            return [];
        }
        var fresh = current.Where(s => !seen.Contains(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();
        seen.UnionWith(current);
        return fresh.Count > MaxNewAtOnce ? [] : fresh.Select(s => new NewListing(exchange, s)).ToList();
    }

    private static async Task<IReadOnlyCollection<string>?> TryFetchAsync(
        Func<CancellationToken, Task<IReadOnlyCollection<string>?>> fetch, CancellationToken ct)
    {
        try
        {
            return await fetch(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    // ---- Sources (every quote currency; only markets that are trading) -------------------

    internal static async Task<IReadOnlyCollection<string>?> FetchBinanceAsync(CancellationToken ct) =>
        ParseBinance(await GetAsync("https://data-api.binance.vision/api/v3/exchangeInfo?permissions=SPOT&symbolStatus=TRADING", ct).ConfigureAwait(false));

    internal static async Task<IReadOnlyCollection<string>?> FetchCoinbaseAsync(CancellationToken ct) =>
        ParseCoinbase(await GetAsync("https://api.exchange.coinbase.com/products", ct).ConfigureAwait(false));

    internal static async Task<IReadOnlyCollection<string>?> FetchKrakenAsync(CancellationToken ct) =>
        ParseKraken(await GetAsync("https://api.kraken.com/0/public/AssetPairs", ct).ConfigureAwait(false));

    internal static async Task<IReadOnlyCollection<string>?> FetchBybitAsync(CancellationToken ct) =>
        ParseBybit(await GetAsync("https://api.bybit.com/v5/market/instruments-info?category=spot", ct).ConfigureAwait(false));

    private static async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var response = await Http.Client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    // {"symbols":[{"baseAsset":"BTC","status":"TRADING",...}]}
    internal static IReadOnlyCollection<string>? ParseBinance(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty("symbols", out var symbols) && symbols.ValueKind == JsonValueKind.Array
            ? Bases(symbols.EnumerateArray()
                .Where(s => Json.String(s, "status") is null or "TRADING")
                .Select(s => Json.String(s, "baseAsset")))
            : null;

    // [{"base_currency":"BTC","status":"online","trading_disabled":false}, ...]
    internal static IReadOnlyCollection<string>? ParseCoinbase(JsonElement json) =>
        json.ValueKind == JsonValueKind.Array
            ? Bases(json.EnumerateArray()
                .Where(p => Json.String(p, "status") is null or "online"
                            && !(p.TryGetProperty("trading_disabled", out var d) && d.ValueKind == JsonValueKind.True))
                .Select(p => Json.String(p, "base_currency")))
            : null;

    // {"result":{"XXBTZUSD":{"wsname":"XBT/USD","status":"online"}}}
    internal static IReadOnlyCollection<string>? ParseKraken(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("result", out var pairs) || pairs.ValueKind != JsonValueKind.Object)
            return null;
        return Bases(pairs.EnumerateObject()
            .Where(p => Json.String(p.Value, "status") is null or "online")
            .Select(p => Json.String(p.Value, "wsname")?.Split('/')[0] switch
            {
                "XBT" => "BTC",
                "XDG" => "DOGE",
                var b => b,
            }));
    }

    // {"retCode":0,"result":{"list":[{"baseCoin":"BTC","status":"Trading"}]}}
    internal static IReadOnlyCollection<string>? ParseBybit(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty("result", out var result)
        && result.ValueKind == JsonValueKind.Object && result.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array
            ? Bases(list.EnumerateArray()
                .Where(s => Json.String(s, "status") is null or "Trading")
                .Select(s => Json.String(s, "baseCoin")))
            : null;

    private static HashSet<string> Bases(IEnumerable<string?> symbols) =>
        symbols.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim().ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // ---- File --------------------------------------------------------------------------

    private Dictionary<string, HashSet<string>> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new(StringComparer.OrdinalIgnoreCase);
            var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path), FileJson);
            return raw?.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save(Dictionary<string, HashSet<string>> known)
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(known.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(s => s, StringComparer.Ordinal).ToList()), FileJson));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remembered for this session anyway.
        }
    }
}
