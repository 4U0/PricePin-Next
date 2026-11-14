using PricePin.Core.Market;

namespace PricePin.Core.Tests;

public class MarketDataServiceTests
{
    private sealed class FakeFeed(string name, Func<int, FeedFailure?> behaviour, bool streaming = true) : IMarketFeed
    {
        public int Attempts;
        public string Name => name;
        public bool IsStreaming => streaming;

        public async Task RunSessionAsync(IReadOnlyList<MarketSymbol> symbols, Action<Ticker> onTick, Action onLive, CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref Attempts);
            if (behaviour(attempt) is { } failure)
                throw new FeedException(failure, $"{name} failed");

            onLive();
            onTick(new Ticker { Base = "BTC", Last = 1m, Source = name });
            await Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private static async Task<FeedStatus> WaitForLive(MarketDataService service)
    {
        var tcs = new TaskCompletionSource<FeedStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += s =>
        {
            if (s.State is FeedState.Live or FeedState.Polling)
                tcs.TrySetResult(s);
        };
        service.Start([new MarketSymbol("BTC", "USD")], DataSource.Auto);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static MarketDataService Create(IReadOnlyList<IMarketFeed> streams, IReadOnlyList<IMarketFeed> rest) =>
        new(_ => streams, rest) { RetryDelay = _ => TimeSpan.Zero, OfflineDelay = TimeSpan.Zero };

    [Fact]
    public async Task Uses_first_feed_when_it_works()
    {
        var a = new FakeFeed("A", _ => null);
        var b = new FakeFeed("B", _ => null);
        await using var service = Create([a, b], []);

        var status = await WaitForLive(service);

        Assert.Equal("A", status.Source);
        Assert.Equal(0, b.Attempts);
    }

    [Fact]
    public async Task Switches_immediately_when_blocked()
    {
        var a = new FakeFeed("A", _ => FeedFailure.Blocked);
        var b = new FakeFeed("B", _ => null);
        await using var service = Create([a, b], []);

        var status = await WaitForLive(service);

        Assert.Equal("B", status.Source);
        Assert.Equal(1, a.Attempts);
    }

    [Fact]
    public async Task Retries_then_switches_after_repeated_failures()
    {
        var a = new FakeFeed("A", _ => FeedFailure.Transient);
        var b = new FakeFeed("B", _ => null);
        await using var service = Create([a, b], []);

        var status = await WaitForLive(service);

        Assert.Equal("B", status.Source);
        Assert.Equal(3, a.Attempts);
    }

    [Fact]
    public async Task Recovers_on_same_feed_after_one_drop()
    {
        var a = new FakeFeed("A", attempt => attempt == 1 ? FeedFailure.Transient : null);
        var b = new FakeFeed("B", _ => null);
        await using var service = Create([a, b], []);

        var status = await WaitForLive(service);

        Assert.Equal("A", status.Source);
        Assert.Equal(0, b.Attempts);
    }

    [Fact]
    public async Task Falls_back_to_rest_when_no_stream_works()
    {
        var a = new FakeFeed("A", _ => FeedFailure.Blocked);
        var b = new FakeFeed("B", _ => FeedFailure.Transient);
        var rest = new FakeFeed("REST", _ => null, streaming: false);
        await using var service = Create([a, b], [rest]);

        var status = await WaitForLive(service);

        Assert.Equal(FeedState.Polling, status.State);
        Assert.Equal("REST", status.Source);
    }

    [Theory]
    [InlineData(1, 850, 1150)]
    [InlineData(3, 3400, 4600)]
    [InlineData(20, 25500, 34500)]
    public void Backoff_grows_and_is_capped(int failures, int minMs, int maxMs)
    {
        var ms = MarketDataService.Backoff(failures).TotalMilliseconds;
        Assert.InRange(ms, minMs, maxMs);
    }
}
