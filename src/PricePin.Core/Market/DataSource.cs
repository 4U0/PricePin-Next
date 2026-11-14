namespace PricePin.Core.Market;

/// <summary>Which exchange to stream from.</summary>
public enum DataSource
{
    /// <summary>Try Binance, then Coinbase, then Kraken, then REST polling.</summary>
    Auto,
    Binance,
    Coinbase,
    Kraken,
}
