namespace PricePin.Core.Market;

/// <summary>
/// A price source. <see cref="RunSessionAsync"/> runs one connection from start to
/// finish; the <see cref="MarketDataService"/> decides when to retry or switch feeds.
/// </summary>
public interface IMarketFeed
{
    /// <summary>Display name, e.g. "Binance".</summary>
    string Name { get; }

    /// <summary>True for push (WebSocket) feeds, false for REST polling.</summary>
    bool IsStreaming { get; }

    /// <summary>
    /// Connects, subscribes to <paramref name="symbols"/> and reports every update
    /// through <paramref name="onTick"/> until the connection ends.
    /// Returns normally only when <paramref name="cancellationToken"/> is cancelled;
    /// otherwise throws <see cref="FeedException"/> describing why it stopped.
    /// </summary>
    /// <param name="onLive">Called once the first data arrives.</param>
    Task RunSessionAsync(
        IReadOnlyList<MarketSymbol> symbols,
        Action<Ticker> onTick,
        Action onLive,
        CancellationToken cancellationToken);
}

public enum FeedFailure
{
    /// <summary>Network trouble or the server closed the connection. Worth retrying.</summary>
    Transient,

    /// <summary>The exchange refuses this user (for example HTTP 451 for restricted regions). Don't retry soon.</summary>
    Blocked,

    /// <summary>Connected but nothing arrived for too long.</summary>
    Stale,
}

public sealed class FeedException(FeedFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public FeedFailure Failure { get; } = failure;
}
