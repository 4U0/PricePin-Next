using PricePin.Core.Market;

namespace PricePin.Core.Feeds;

/// <summary>
/// Last-resort feed: asks a REST API for prices every few seconds.
/// Used only while no WebSocket feed can connect.
/// </summary>
public abstract class RestPollingFeedBase : IMarketFeed
{
    private const int MaxConsecutiveFailures = 3;

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);

    public abstract string Name { get; }

    public bool IsStreaming => false;

    /// <summary>Fetches one round of prices. Throws on HTTP or network errors.</summary>
    protected abstract Task<IReadOnlyList<Ticker>> FetchAsync(IReadOnlyList<MarketSymbol> symbols, CancellationToken ct);

    public async Task RunSessionAsync(
        IReadOnlyList<MarketSymbol> symbols,
        Action<Ticker> onTick,
        Action onLive,
        CancellationToken cancellationToken)
    {
        var failures = 0;
        var live = false;
        using var timer = new PeriodicTimer(Interval);

        try
        {
            do
            {
                try
                {
                    var tickers = await FetchAsync(symbols, cancellationToken).ConfigureAwait(false);
                    failures = 0;
                    if (!live && tickers.Count > 0)
                    {
                        live = true;
                        onLive();
                    }
                    foreach (var t in tickers)
                        onTick(t);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                           && !cancellationToken.IsCancellationRequested)
                {
                    var fe = Http.ToFeedException(Name, ex);
                    if (fe.Failure == FeedFailure.Blocked || ++failures >= MaxConsecutiveFailures)
                        throw fe;
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped on purpose.
        }
    }
}
