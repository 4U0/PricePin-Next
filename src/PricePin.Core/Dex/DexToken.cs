using System.Text.Json.Serialization;
using PricePin.Core.Coins;

namespace PricePin.Core.Dex;

/// <summary>
/// A token from a decentralised exchange that the user added by search or contract address.
/// Saved in the settings so its name, logo and trading pair are known without a lookup.
/// </summary>
public sealed class DexToken
{
    /// <summary>DexScreener chain id, e.g. "solana", "ethereum", "bsc", "base".</summary>
    public string Chain { get; set; } = "";

    /// <summary>Contract (mint) address.</summary>
    public string Address { get; set; } = "";

    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The pool prices are read from: the most liquid one when the token was added.</summary>
    public string? PairAddress { get; set; }

    /// <summary>Exchange of that pool, e.g. "raydium", "uniswap".</summary>
    public string? DexId { get; set; }

    public string? ImageUrl { get; set; }

    [JsonIgnore]
    public string Key => CoinKey.ForDex(Chain, Address);

    [JsonIgnore]
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Chain) && !string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(Symbol)
        && Chain.Length <= 40 && Address.Length <= 200;

    public void Normalize()
    {
        Chain = Chain.Trim().ToLowerInvariant();
        CoinKey.TryParseDex(CoinKey.ForDex(Chain, Address), out _, out var address);
        Address = address;
        Symbol = Clean(Symbol, 16);
        Name = Clean(string.IsNullOrWhiteSpace(Name) ? Symbol : Name, 60);
        if (ImageUrl is not null && !ImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ImageUrl = null;
    }

    private static string Clean(string text, int max)
    {
        var s = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length > max ? s[..max] : s;
    }
}

/// <summary>Display names for chains, and their ids on GeckoTerminal (used for charts).</summary>
public static class DexChains
{
    private static readonly Dictionary<string, (string Name, string Gecko)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["solana"] = ("Solana", "solana"),
        ["ethereum"] = ("Ethereum", "eth"),
        ["bsc"] = ("BNB Chain", "bsc"),
        ["base"] = ("Base", "base"),
        ["arbitrum"] = ("Arbitrum", "arbitrum"),
        ["polygon"] = ("Polygon", "polygon_pos"),
        ["avalanche"] = ("Avalanche", "avax"),
        ["optimism"] = ("Optimism", "optimism"),
        ["sui"] = ("Sui", "sui-network"),
        ["ton"] = ("TON", "ton"),
        ["tron"] = ("Tron", "tron"),
        ["pulsechain"] = ("PulseChain", "pulsechain"),
        ["blast"] = ("Blast", "blast"),
        ["linea"] = ("Linea", "linea"),
        ["zksync"] = ("zkSync", "zksync"),
        ["fantom"] = ("Fantom", "ftm"),
        ["cronos"] = ("Cronos", "cro"),
        ["mantle"] = ("Mantle", "mantle"),
        ["scroll"] = ("Scroll", "scroll"),
        ["sonic"] = ("Sonic", "sonic"),
        ["hyperevm"] = ("HyperEVM", "hyperevm"),
        ["abstract"] = ("Abstract", "abstract"),
        ["berachain"] = ("Berachain", "berachain"),
        ["unichain"] = ("Unichain", "unichain"),
    };

    public static string DisplayName(string chain) =>
        Known.TryGetValue(chain, out var c) ? c.Name
        : chain.Length == 0 ? chain
        : char.ToUpperInvariant(chain[0]) + chain[1..];

    public static string GeckoTerminalNetwork(string chain) => Known.TryGetValue(chain, out var c) ? c.Gecko : chain.ToLowerInvariant();
}
