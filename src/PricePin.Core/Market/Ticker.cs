namespace PricePin.Core.Market;

/// <summary>
/// One price update from a feed. Only <see cref="Last"/> is always present: trade
/// streams carry just a price, while ticker streams also carry 24h stats.
/// </summary>
public sealed record Ticker
{
    public required string Base { get; init; }
    public required decimal Last { get; init; }

    public decimal? Open24h { get; init; }
    public decimal? High24h { get; init; }
    public decimal? Low24h { get; init; }

    /// <summary>24h traded volume in the base coin.</summary>
    public decimal? Volume24h { get; init; }

    public decimal? ChangePercent24h { get; init; }

    /// <summary>When the exchange produced the update (UTC), if it says.</summary>
    public DateTimeOffset? ExchangeTime { get; init; }

    /// <summary>Name of the feed that produced it, e.g. "Binance".</summary>
    public required string Source { get; init; }
}
