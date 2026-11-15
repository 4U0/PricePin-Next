using PricePin.Core.History;

namespace PricePin.Core.Alerts;

/// <summary>
/// Alerts that watch for unusual activity on the user's coins instead of a fixed price:
/// sudden moves, volume spikes, and new coins listed on the big exchanges.
/// </summary>
public sealed class SmartAlertSettings
{
    /// <summary>Notify when a coin moves <see cref="FastMovePercent"/> within <see cref="FastMoveMinutes"/>.</summary>
    public bool FastMove { get; set; }
    public decimal FastMovePercent { get; set; } = 3m;
    public int FastMoveMinutes { get; set; } = 15;

    /// <summary>Notify when a coin trades <see cref="VolumeSpikeFactor"/> times its normal hourly volume.</summary>
    public bool VolumeSpike { get; set; }
    public decimal VolumeSpikeFactor { get; set; } = 5m;

    /// <summary>Notify when Binance, Coinbase, Kraken or Bybit starts trading a new coin.</summary>
    public bool NewListings { get; set; }

    public static IReadOnlyList<decimal> MovePercents { get; } = [2m, 3m, 5m, 10m];
    public static IReadOnlyList<int> MoveMinutes { get; } = [5, 15, 60];
    public static IReadOnlyList<decimal> SpikeFactors { get; } = [3m, 5m, 10m];

    public void Normalize()
    {
        FastMovePercent = MovePercents.OrderBy(p => Math.Abs(p - FastMovePercent)).First();
        FastMoveMinutes = MoveMinutes.OrderBy(m => Math.Abs(m - FastMoveMinutes)).First();
        VolumeSpikeFactor = SpikeFactors.OrderBy(f => Math.Abs(f - VolumeSpikeFactor)).First();
    }
}

/// <param name="ChangePercent">Signed: +4.2 is up, -4.2 is down.</param>
public sealed record FastMove(string Symbol, decimal ChangePercent, decimal From, decimal To, TimeSpan Window);

/// <summary>
/// Spots a coin moving a set percentage within a few minutes, from the live prices. It compares the
/// newest price with the lowest and highest in the window. After it fires for a coin it stays quiet
/// for that coin for at least 15 minutes, so one big candle sends one notification.
/// </summary>
public sealed class FastMoveDetector
{
    /// <summary>Prices are sampled at most this often per coin, which keeps an hour's window small.</summary>
    public static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MinQuiet = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, Queue<(DateTimeOffset Time, decimal Price)>> _samples = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _quietUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastSample = new(StringComparer.OrdinalIgnoreCase);

    public FastMove? Add(string symbol, decimal price, DateTimeOffset now, decimal percent, TimeSpan window)
    {
        if (price <= 0 || percent <= 0 || window <= TimeSpan.Zero)
            return null;
        if (!_samples.TryGetValue(symbol, out var q))
            _samples[symbol] = q = new Queue<(DateTimeOffset, decimal)>();

        while (q.Count > 0 && now - q.Peek().Time > window)
            q.Dequeue();

        FastMove? move = null;
        if (q.Count > 0 && !(_quietUntil.TryGetValue(symbol, out var quiet) && now < quiet))
        {
            decimal low = decimal.MaxValue, high = 0;
            foreach (var (_, p) in q)
            {
                low = Math.Min(low, p);
                high = Math.Max(high, p);
            }
            var up = (price - low) / low * 100m;
            var down = (price - high) / high * 100m;
            if (up >= percent)
                move = new FastMove(symbol, Math.Round(up, 2), low, price, window);
            else if (-down >= percent)
                move = new FastMove(symbol, Math.Round(down, 2), high, price, window);
        }

        if (move is not null)
        {
            _quietUntil[symbol] = now + (window > MinQuiet ? window : MinQuiet);
            q.Clear();
        }
        if (q.Count == 0 || now - _lastSample.GetValueOrDefault(symbol) >= SampleEvery)
        {
            q.Enqueue((now, price));
            _lastSample[symbol] = now;
        }
        return move;
    }

    /// <summary>Forgets everything, e.g. after the display currency changed.</summary>
    public void Reset()
    {
        _samples.Clear();
        _quietUntil.Clear();
        _lastSample.Clear();
    }

}

/// <param name="Ratio">The last hour's volume divided by the average hour over the day before.</param>
/// <param name="ChangePercent">Price change over that last hour.</param>
public sealed record VolumeSpike(string Symbol, decimal Ratio, decimal ChangePercent);

public static class VolumeSpikes
{
    /// <summary>Volume data older than this is not trusted to say anything about "now".</summary>
    public static readonly TimeSpan MaxStaleness = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Compares the last hour's traded volume with the average hour of the 24 hours before it.
    /// <paramref name="candles"/> are the 15-minute candles of the 1D chart, oldest first.
    /// </summary>
    public static VolumeSpike? Check(string symbol, IReadOnlyList<Candle> candles, decimal factor, DateTimeOffset now)
    {
        if (candles.Count < 40 || factor <= 0 || now - candles[^1].Time > MaxStaleness)
            return null;
        var hourAgo = now - TimeSpan.FromHours(1);
        var recent = candles.Where(c => c.Time >= hourAgo).ToList();
        var earlier = candles.Where(c => c.Time < hourAgo).ToList();
        if (recent.Count == 0 || earlier.Count < 32)
            return null;

        var spacing = (earlier[^1].Time - earlier[0].Time) / Math.Max(1, earlier.Count - 1);
        if (spacing <= TimeSpan.Zero)
            return null;
        var hours = (decimal)(spacing.TotalHours * earlier.Count);
        var averageHour = earlier.Sum(c => c.Volume) / hours;
        if (averageHour <= 0)
            return null;

        var ratio = recent.Sum(c => c.Volume) / averageHour;
        if (ratio < factor)
            return null;
        var open = recent[0].Open;
        var change = open > 0 ? Math.Round((recent[^1].Close - open) / open * 100m, 2) : 0m;
        return new VolumeSpike(symbol, Math.Round(ratio, 1), change);
    }
}
