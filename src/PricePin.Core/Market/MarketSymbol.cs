namespace PricePin.Core.Market;

/// <summary>
/// A trading pair as the user thinks of it, e.g. BTC priced in USD.
/// Each feed maps this to its own naming (BTCUSDT, BTC-USD, BTC/USD).
/// </summary>
public readonly record struct MarketSymbol
{
    public MarketSymbol(string @base, string quote)
    {
        Base = Normalize(@base);
        Quote = Normalize(quote);
    }

    public string Base { get; }
    public string Quote { get; }

    public override string ToString() => $"{Base}/{Quote}";

    private static string Normalize(string s)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(s);
        return s.Trim().ToUpperInvariant();
    }
}
