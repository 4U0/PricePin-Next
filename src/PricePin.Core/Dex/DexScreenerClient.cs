using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Coins;
using PricePin.Core.Feeds;

namespace PricePin.Core.Dex;

/// <summary>One trading pool for a token on a decentralised exchange, as DexScreener reports it.</summary>
public sealed record DexPair
{
    public required string Chain { get; init; }
    public required string DexId { get; init; }
    public required string PairAddress { get; init; }
    public required string BaseAddress { get; init; }
    public required string BaseSymbol { get; init; }
    public required string BaseName { get; init; }
    public string QuoteSymbol { get; init; } = "";
    public decimal? PriceUsd { get; init; }
    public decimal? Change1h { get; init; }
    public decimal? Change24h { get; init; }
    public decimal? VolumeUsd24h { get; init; }
    public decimal? LiquidityUsd { get; init; }
    public decimal? MarketCapUsd { get; init; }
    public string? ImageUrl { get; init; }
    public string? Url { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public string Key => CoinKey.ForDex(Chain, BaseAddress);

    public DexToken ToToken() => new()
    {
        Chain = Chain,
        Address = BaseAddress,
        Symbol = BaseSymbol,
        Name = BaseName,
        PairAddress = PairAddress,
        DexId = DexId,
        ImageUrl = ImageUrl,
    };
}

/// <summary>
/// DexScreener's free public API (no key; about 300 requests a minute). Covers tokens on
/// Solana, Ethereum, BNB Chain, Base and dozens of other chains, including brand-new ones.
/// </summary>
public static class DexScreenerClient
{
    private const string Api = "https://api.dexscreener.com";

    /// <summary>Pools with less liquidity than this are left out of search results (mostly spam and rugs).</summary>
    public const decimal MinSearchLiquidityUsd = 5_000m;

    /// <summary>Addresses per request to the tokens endpoint.</summary>
    public const int MaxAddressesPerRequest = 30;

    /// <summary>
    /// Finds tokens by name, symbol or contract address. Returns one entry per token (its most
    /// liquid pool), best first.
    /// </summary>
    public static async Task<IReadOnlyList<DexPair>> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length < 2)
            return [];
        var json = await GetJsonAsync($"{Api}/latest/dex/search?q={Uri.EscapeDataString(query)}", ct).ConfigureAwait(false);
        if (json is not { } j || !j.TryGetProperty("pairs", out var pairs))
            return [];
        var isAddress = CoinKey.LooksLikeAddress(query);
        return BestPerToken(ParsePairs(pairs), isAddress ? 0 : MinSearchLiquidityUsd)
            // Searching by address should find that token, not pools that merely trade against it.
            .Where(p => !isAddress || string.Equals(p.BaseAddress, query, StringComparison.OrdinalIgnoreCase))
            .Take(15)
            .ToList();
    }

    /// <summary>Current pools for up to 30 tokens on one chain.</summary>
    public static async Task<IReadOnlyList<DexPair>> GetTokenPairsAsync(string chain, IReadOnlyList<string> addresses, CancellationToken ct = default)
    {
        if (addresses.Count == 0)
            return [];
        var list = string.Join(",", addresses.Take(MaxAddressesPerRequest).Select(Uri.EscapeDataString));
        var json = await GetJsonAsync($"{Api}/tokens/v1/{Uri.EscapeDataString(chain)}/{list}", ct).ConfigureAwait(false);
        return json is { ValueKind: JsonValueKind.Array } j ? ParsePairs(j) : [];
    }

    /// <summary>
    /// Picks the pool to price each token from: the saved one if it still exists, otherwise the
    /// most liquid pool where the token is the base (not the quote) currency.
    /// </summary>
    public static Dictionary<string, DexPair> PickPairs(IEnumerable<DexToken> tokens, IReadOnlyList<DexPair> pairs)
    {
        var result = new Dictionary<string, DexPair>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var mine = pairs.Where(p => p.Chain.Equals(token.Chain, StringComparison.OrdinalIgnoreCase)
                                        && p.BaseAddress.Equals(token.Address, StringComparison.OrdinalIgnoreCase)
                                        && p.PriceUsd is > 0)
                .ToList();
            var pick = mine.FirstOrDefault(p => token.PairAddress is not null && p.PairAddress.Equals(token.PairAddress, StringComparison.OrdinalIgnoreCase))
                       ?? mine.OrderByDescending(p => p.LiquidityUsd ?? 0).FirstOrDefault();
            if (pick is not null)
                result[token.Key] = pick;
        }
        return result;
    }

    internal static IReadOnlyList<DexPair> BestPerToken(IEnumerable<DexPair> pairs, decimal minLiquidity) =>
        pairs.Where(p => p.PriceUsd is > 0 && (p.LiquidityUsd ?? 0) >= minLiquidity)
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.LiquidityUsd ?? 0).First())
            .OrderByDescending(p => (p.LiquidityUsd ?? 0) + (p.VolumeUsd24h ?? 0))
            .ToList();

    internal static IReadOnlyList<DexPair> ParsePairs(JsonElement array)
    {
        var list = new List<DexPair>();
        if (array.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var p in array.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object
                || Json.String(p, "chainId") is not { } chain
                || Json.String(p, "pairAddress") is not { } pairAddress
                || !p.TryGetProperty("baseToken", out var bt)
                || Json.String(bt, "address") is not { } baseAddress
                || Json.String(bt, "symbol") is not { Length: > 0 } baseSymbol)
                continue;

            list.Add(new DexPair
            {
                Chain = chain,
                DexId = Json.String(p, "dexId") ?? "",
                PairAddress = pairAddress,
                BaseAddress = baseAddress,
                BaseSymbol = baseSymbol,
                BaseName = Json.String(bt, "name") ?? baseSymbol,
                QuoteSymbol = p.TryGetProperty("quoteToken", out var qt) ? Json.String(qt, "symbol") ?? "" : "",
                PriceUsd = Json.Decimal(p, "priceUsd"),
                Change1h = Child(p, "priceChange", "h1"),
                Change24h = Child(p, "priceChange", "h24"),
                VolumeUsd24h = Child(p, "volume", "h24"),
                LiquidityUsd = Child(p, "liquidity", "usd"),
                MarketCapUsd = Json.Decimal(p, "marketCap") ?? Json.Decimal(p, "fdv"),
                ImageUrl = p.TryGetProperty("info", out var info) ? Json.String(info, "imageUrl") : null,
                Url = Json.String(p, "url"),
                CreatedAt = Json.UnixMs(p, "pairCreatedAt"),
            });
        }
        return list;
    }

    private static decimal? Child(JsonElement obj, string name, string field) =>
        obj.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object ? Json.Decimal(child, field) : null;

    private static async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await Http.Client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
    }
}
