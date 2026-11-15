using System.Collections.Concurrent;
using PricePin.Core.Dex;

namespace PricePin.Core.Coins;

/// <param name="Symbol">Ticker symbol, e.g. "BTC".</param>
/// <param name="Name">Full name, e.g. "Bitcoin".</param>
/// <param name="BrandColor">Hex colour used for the fallback badge, e.g. "#F7931A".</param>
public sealed record CoinInfo(string Symbol, string Name, string BrandColor);

/// <summary>
/// Names and colours for well-known coins. Unknown coins still work; they just show
/// their symbol as the name and a neutral badge until a logo is found.
/// </summary>
public static class CoinCatalog
{
    private static readonly Dictionary<string, CoinInfo> Known = new CoinInfo[]
    {
        new("BTC", "Bitcoin", "#F7931A"),
        new("ETH", "Ethereum", "#627EEA"),
        new("SOL", "Solana", "#9945FF"),
        new("BNB", "BNB", "#F3BA2F"),
        new("XRP", "XRP", "#23292F"),
        new("DOGE", "Dogecoin", "#C2A633"),
        new("ADA", "Cardano", "#0033AD"),
        new("TRX", "TRON", "#EB0029"),
        new("AVAX", "Avalanche", "#E84142"),
        new("LINK", "Chainlink", "#2A5ADA"),
        new("DOT", "Polkadot", "#E6007A"),
        new("LTC", "Litecoin", "#345D9D"),
        new("BCH", "Bitcoin Cash", "#8DC351"),
        new("TON", "Toncoin", "#0098EA"),
        new("SHIB", "Shiba Inu", "#FFA409"),
        new("XLM", "Stellar", "#14B6E7"),
        new("SUI", "Sui", "#4DA2FF"),
        new("NEAR", "NEAR Protocol", "#00C08B"),
        new("UNI", "Uniswap", "#FF007A"),
        new("ATOM", "Cosmos", "#2E3148"),
        new("PEPE", "Pepe", "#4C9540"),
        new("XMR", "Monero", "#FF6600"),
        new("ETC", "Ethereum Classic", "#328332"),
        new("APT", "Aptos", "#06F7F7"),
        new("ARB", "Arbitrum", "#28A0F0"),
        new("OP", "Optimism", "#FF0420"),
        new("FIL", "Filecoin", "#0090FF"),
        new("HBAR", "Hedera", "#3D3D3D"),
        new("ICP", "Internet Computer", "#29ABE2"),
        new("AAVE", "Aave", "#B6509E"),
    }.ToDictionary(c => c.Symbol, StringComparer.OrdinalIgnoreCase);

    /// <summary>Position in the well-known list (lower is more popular), or int.MaxValue.</summary>
    public static int PopularityRank(string symbol)
    {
        var i = 0;
        foreach (var key in Known.Keys)
        {
            if (string.Equals(key, symbol, StringComparison.OrdinalIgnoreCase))
                return i;
            i++;
        }
        return int.MaxValue;
    }

    public static bool IsKnown(string symbol) => Known.ContainsKey(symbol);

    /// <summary>The well-known coins, most popular first.</summary>
    public static IEnumerable<CoinInfo> All => Known.Values;

    private static readonly ConcurrentDictionary<string, DexToken> Tokens = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] TokenColors =
        ["#6366F1", "#0EA5E9", "#14B8A6", "#F59E0B", "#EF4444", "#8B5CF6", "#EC4899", "#10B981"];

    /// <summary>Makes a DEX token's symbol, name and logo known to every window.</summary>
    public static void Register(DexToken token)
    {
        if (token.IsValid)
            Tokens[token.Key] = token;
    }

    public static DexToken? GetToken(string? key) =>
        key is not null && Tokens.TryGetValue(CoinKey.Normalize(key), out var t) ? t : null;

    /// <summary>
    /// Symbol, name and badge colour for a coin key. DEX tokens show their own symbol;
    /// an unknown DEX key shows a shortened address until the token is looked up.
    /// </summary>
    public static CoinInfo Get(string key)
    {
        if (Known.TryGetValue(key, out var info))
            return info;
        if (CoinKey.TryParseDex(key, out var chain, out var address))
        {
            var color = TokenColors[(int)((uint)StableHash(key) % TokenColors.Length)];
            return GetToken(key) is { } t
                ? new CoinInfo(t.Symbol, t.Name, color)
                : new CoinInfo(Shorten(address), DexChains.DisplayName(chain), color);
        }
        return new CoinInfo(key.ToUpperInvariant(), key.ToUpperInvariant(), "#64748B");
    }

    /// <summary>The symbol to show for a coin key: "BTC", or a DEX token's own symbol.</summary>
    public static string DisplaySymbol(string key) => Get(key).Symbol;

    /// <summary>
    /// Turns what someone typed in a coin box into a coin key: "btc" becomes "BTC", and a DEX
    /// token's symbol becomes its key when one of <paramref name="candidates"/> (or a saved token) has it.
    /// </summary>
    public static string ResolveTyped(string? text, IEnumerable<string> candidates)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0 || CoinKey.IsDex(text))
            return text.Length == 0 ? "" : CoinKey.Normalize(text);
        var list = candidates.ToList();
        if (list.FirstOrDefault(k => !CoinKey.IsDex(k) && string.Equals(k, text, StringComparison.OrdinalIgnoreCase)) is { } exchange)
            return exchange;
        if (list.Where(CoinKey.IsDex).FirstOrDefault(k => string.Equals(DisplaySymbol(k), text, StringComparison.OrdinalIgnoreCase)) is { } dex)
            return dex;
        var saved = Tokens.Values.Where(t => string.Equals(t.Symbol, text, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return saved.Count == 1 ? saved[0].Key : text.ToUpperInvariant();
    }

    /// <summary>True for a key the alert and portfolio forms accept: a 2–12 character symbol or a DEX key.</summary>
    public static bool IsValidKey(string key) =>
        CoinKey.IsDex(key) ? CoinKey.TryParseDex(key, out _, out _) : key.Length is >= 2 and <= 12 && key.All(char.IsAsciiLetterOrDigit);

    /// <summary>"7xKX…gAsU".</summary>
    public static string Shorten(string address) =>
        address.Length <= 12 ? address : address[..4] + "…" + address[^4..];

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 23;
            foreach (var c in s)
                h = h * 31 + c;
            return h;
        }
    }

    /// <summary>The coins shown on first run.</summary>
    public static IReadOnlyList<string> Defaults { get; } = ["BTC", "ETH", "SOL", "BNB", "XRP", "DOGE", "ADA"];
}
