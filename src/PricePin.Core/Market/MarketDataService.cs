using PricePin.Core.Feeds;

namespace PricePin.Core.Market;

/// <summary>
/// Keeps prices flowing. Runs one feed at a time and, when it fails:
/// <list type="bullet">
/// <item>retries the same feed with growing waits (1s, 2s, 4s … up to 30s, with jitter),</item>
/// <item>after repeated failures, or at once if the feed is blocked in the user's region,
/// moves on to the next feed (Auto mode only),</item>
/// <item>if every WebSocket feed fails, polls REST for a few minutes, then tries streaming again.</item>
/// </list>
/// Ticks and status changes are raised on a background thread.
/// </summary>
public sealed class MarketDataService : IAsyncDisposable
{
    private const int FailuresBeforeSwitching = 3;
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BlockedCooldown = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PollingBeforeRetryingStreams = TimeSpan.FromMinutes(3);

    private readonly Func<DataSource, IReadOnlyList<IMarketFeed>> _streamFeeds;
    private readonly IReadOnlyList<IMarketFeed> _restFeeds;
    private readonly Dictionary<string, DateTimeOffset> _blockedUntil = new();
    private readonly object _gate = new();

    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private IReadOnlyList<MarketSymbol> _symbols = [];
    private DataSource _source = DataSource.Auto;

    public MarketDataService()
        : this(DefaultStreamFeeds, [new BinanceRestFeed(), new CoinbaseRestFeed()])
    {
    }

    public MarketDataService(Func<DataSource, IReadOnlyList<IMarketFeed>> streamFeeds, IReadOnlyList<IMarketFeed> restFeeds)
    {
        _streamFeeds = streamFeeds;
        _restFeeds = restFeeds;
    }

    /// <summary>Wait before retry number n (1-based). Replaceable so tests don't sleep.</summary>
    internal Func<int, TimeSpan> RetryDelay { get; set; } = Backoff;

    /// <summary>Wait after nothing at all could connect.</summary>
    internal TimeSpan OfflineDelay { get; set; } = MaxBackoff;

    public event Action<Ticker>? TickReceived;
    public event Action<FeedStatus>? StatusChanged;

    public FeedStatus Status { get; private set; } = new(FeedState.Offline, "—");

    /// <summary>Starts streaming, or restarts with new coins or a new source.</summary>
    public void Start(IReadOnlyList<MarketSymbol> symbols, DataSource source)
    {
        lock (_gate)
        {
            _runCts?.Cancel();
            _symbols = symbols.Distinct().ToList();
            _source = source;
            _runCts = new CancellationTokenSource();
            var token = _runCts.Token;
            var previous = _runTask;
            _runTask = Task.Run(async () =>
            {
                if (previous is not null)
                    await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await RunAsync(token).ConfigureAwait(false);
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? task;
        lock (_gate)
        {
            _runCts?.Cancel();
            task = _runTask;
        }
        if (task is not null)
            await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _runCts?.Dispose();
    }

    public static IReadOnlyList<IMarketFeed> DefaultStreamFeeds(DataSource source) => source switch
    {
        DataSource.Binance => [new BinanceFeed()],
        DataSource.Coinbase => [new CoinbaseFeed()],
        DataSource.Kraken => [new KrakenFeed()],
        _ => [new BinanceFeed(), new CoinbaseFeed(), new KrakenFeed()],
    };

    private async Task RunAsync(CancellationToken ct)
    {
        if (_symbols.Count == 0)
        {
            SetStatus(new FeedStatus(FeedState.Offline, "—", "No coins selected"));
            return;
        }

        var feeds = _streamFeeds(_source);
        var autoSwitch = _source == DataSource.Auto;

        while (!ct.IsCancellationRequested)
        {
            var anyStreamWorked = false;

            foreach (var feed in feeds)
            {
                if (IsBlocked(feed) && autoSwitch)
                    continue;

                var worked = await RunFeedWithRetriesAsync(feed, ct, giveUpAfterFailures: autoSwitch).ConfigureAwait(false);
                anyStreamWorked |= worked;
                if (ct.IsCancellationRequested)
                    return;
            }

            if (ct.IsCancellationRequested)
                return;

            // Every stream failed. Poll REST for a while so prices keep updating, then try streams again.
            if (!anyStreamWorked)
                await RunRestFallbackAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs a feed, reconnecting after drops. Returns when the feed should be given up on
    /// (too many failures in a row, or blocked). Returns true if it ever delivered data.
    /// </summary>
    private async Task<bool> RunFeedWithRetriesAsync(IMarketFeed feed, CancellationToken ct, bool giveUpAfterFailures)
    {
        var failures = 0;
        var everLive = false;

        while (!ct.IsCancellationRequested)
        {
            var wentLive = false;
            SetStatus(new FeedStatus(FeedState.Connecting, feed.Name));
            try
            {
                await feed.RunSessionAsync(_symbols, OnTick, () =>
                {
                    wentLive = true;
                    SetStatus(new FeedStatus(FeedState.Live, feed.Name));
                }, ct).ConfigureAwait(false);
                return everLive || wentLive; // cancelled
            }
            catch (FeedException ex)
            {
                everLive |= wentLive;
                if (ex.Failure == FeedFailure.Blocked)
                {
                    lock (_blockedUntil)
                        _blockedUntil[feed.Name] = DateTimeOffset.UtcNow + BlockedCooldown;
                    SetStatus(new FeedStatus(FeedState.Reconnecting, feed.Name, ex.Message));
                    if (giveUpAfterFailures)
                        return everLive;
                }

                // A session that delivered data and then dropped is a normal reconnect, not a strike.
                failures = wentLive ? 1 : failures + 1;
                if (giveUpAfterFailures && failures >= FailuresBeforeSwitching)
                    return everLive;

                var delay = RetryDelay(failures);
                SetStatus(new FeedStatus(FeedState.Reconnecting, feed.Name, ex.Message, DateTimeOffset.UtcNow + delay));
                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return everLive;
                }
            }
        }
        return everLive;
    }

    private async Task RunRestFallbackAsync(CancellationToken ct)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(PollingBeforeRetryingStreams);

        foreach (var feed in _restFeeds)
        {
            if (IsBlocked(feed))
                continue;

            SetStatus(new FeedStatus(FeedState.Connecting, feed.Name));
            try
            {
                await feed.RunSessionAsync(_symbols, OnTick,
                    () => SetStatus(new FeedStatus(FeedState.Polling, feed.Name)), window.Token).ConfigureAwait(false);
                return; // polling window ended
            }
            catch (FeedException ex)
            {
                if (ex.Failure == FeedFailure.Blocked)
                {
                    lock (_blockedUntil)
                        _blockedUntil[feed.Name] = DateTimeOffset.UtcNow + BlockedCooldown;
                }
            }
        }

        // Nothing works at all: most likely no internet. Wait, then start over.
        var delay = OfflineDelay;
        SetStatus(new FeedStatus(FeedState.Offline, "—", "Can't reach any exchange", DateTimeOffset.UtcNow + delay));
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool IsBlocked(IMarketFeed feed)
    {
        lock (_blockedUntil)
            return _blockedUntil.TryGetValue(feed.Name, out var until) && until > DateTimeOffset.UtcNow;
    }

    internal static TimeSpan Backoff(int failures)
    {
        var exp = MinBackoff.TotalMilliseconds * Math.Pow(2, Math.Max(0, failures - 1));
        var capped = Math.Min(exp, MaxBackoff.TotalMilliseconds);
        var jitter = Random.Shared.NextDouble() * 0.3 * capped;
        return TimeSpan.FromMilliseconds(capped * 0.85 + jitter);
    }

    private void OnTick(Ticker t) => TickReceived?.Invoke(t);

    private void SetStatus(FeedStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }
}
