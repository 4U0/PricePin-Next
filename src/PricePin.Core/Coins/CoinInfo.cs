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

    public static CoinInfo Get(string symbol) =>
        Known.TryGetValue(symbol, out var info) ? info : new CoinInfo(symbol.ToUpperInvariant(), symbol.ToUpperInvariant(), "#64748B");

    /// <summary>The coins shown on first run.</summary>
    public static IReadOnlyList<string> Defaults { get; } = ["BTC", "ETH", "SOL", "BNB", "XRP", "DOGE", "ADA"];
}
