using System.Text.Json;
using PricePin.Core.Market;

namespace PricePin.Core.Dex;

/// <summary>
/// Keeps DEX token prices updating. DexScreener has no free streaming API, so this polls it
/// every few seconds (one request per 30 tokens per chain), converts dollar prices into the
/// chosen currency, and reports them as ordinary <see cref="Ticker"/> updates keyed by
/// the token's coin key. Runs alongside the exchange feed. Events fire on a background thread.
/// </summary>
public sealed class DexPriceService : IAsyncDisposable
{
    public const string SourceName = "DexScreener";

    private readonly object _gate = new();
    private readonly FiatRates _rates;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DexPriceService(FiatRates? rates = null) => _rates = rates ?? FiatRates.Shared;

    /// <summary>Time between polls while things work.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(8);

    public event Action<Ticker>? TickReceived;

    /// <summary>Latest pool data for each token key, e.g. for showing liquidity.</summary>
    public event Action<DexPair>? PairUpdated;

    /// <summary>Starts polling these tokens, or stops when the list is empty.</summary>
    public void Start(IReadOnlyList<DexToken> tokens, string quote)
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts = null;
            if (tokens.Count == 0)
                return;
            var cts = new CancellationTokenSource();
            _cts = cts;
            var previous = _loop;
            var list = tokens.DistinctBy(t => t.Key).ToList();
            _loop = Task.Run(async () =>
            {
                if (previous is not null)
                    await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await RunAsync(list, quote, cts.Token).ConfigureAwait(false);
            });
        }
    }

    private async Task RunAsync(IReadOnlyList<DexToken> tokens, string quote, CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(tokens, quote, ct).ConfigureAwait(false);
                failures = 0;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                failures++;
            }

            var wait = failures == 0 ? Interval : TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, failures - 1)));
            try
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task PollOnceAsync(IReadOnlyList<DexToken> tokens, string quote, CancellationToken ct)
    {
        if (await _rates.UsdToAsync(quote, ct).ConfigureAwait(false) is not { } rate)
            return; // no exchange rate yet: showing dollars as euros would be wrong

        foreach (var chain in tokens.GroupBy(t => t.Chain, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var chunk in chain.Chunk(DexScreenerClient.MaxAddressesPerRequest))
            {
                var pairs = await DexScreenerClient.GetTokenPairsAsync(chain.Key, chunk.Select(t => t.Address).ToList(), ct)
                    .ConfigureAwait(false);
                foreach (var (key, pair) in DexScreenerClient.PickPairs(chunk, pairs))
                {
                    TickReceived?.Invoke(ToTicker(key, pair, rate));
                    PairUpdated?.Invoke(pair);
                }
            }
        }
    }

    internal static Ticker ToTicker(string key, DexPair pair, decimal usdToQuote)
    {
        var price = pair.PriceUsd!.Value * usdToQuote;
        decimal? open = pair.Change24h is { } ch && ch > -100 ? price / (1 + ch / 100m) : null;
        return new Ticker
        {
            Base = key,
            Last = price,
            Open24h = open,
            ChangePercent24h = pair.Change24h,
            ChangePercent1h = pair.Change1h,
            Volume24h = pair.VolumeUsd24h is { } v && pair.PriceUsd > 0 ? v / pair.PriceUsd.Value : null,
            MarketCap = pair.MarketCapUsd * usdToQuote,
            Liquidity = pair.LiquidityUsd * usdToQuote,
            Source = SourceName,
        };
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        lock (_gate)
        {
            _cts?.Cancel();
            loop = _loop;
        }
        if (loop is not null)
            await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
