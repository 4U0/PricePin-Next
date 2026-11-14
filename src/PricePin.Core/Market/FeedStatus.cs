namespace PricePin.Core.Market;

public enum FeedState
{
    /// <summary>Opening a connection.</summary>
    Connecting,

    /// <summary>Streaming live over WebSocket.</summary>
    Live,

    /// <summary>Connection dropped; waiting before the next attempt.</summary>
    Reconnecting,

    /// <summary>No WebSocket feed works right now; polling REST every few seconds.</summary>
    Polling,

    /// <summary>Nothing is reachable (no internet, or every source blocked).</summary>
    Offline,
}

/// <param name="State">What the connection is doing.</param>
/// <param name="Source">Feed in use or being tried, e.g. "Binance".</param>
/// <param name="Detail">Short human-readable note, e.g. "retrying in 4s".</param>
/// <param name="RetryAt">When the next attempt happens, while reconnecting.</param>
public sealed record FeedStatus(FeedState State, string Source, string? Detail = null, DateTimeOffset? RetryAt = null);
