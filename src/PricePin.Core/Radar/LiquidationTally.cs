namespace PricePin.Core.Radar;

/// <summary>
/// Adds up liquidations of every size over the last hour, per coin and minute, so the hourly
/// totals cover the whole market without keeping each small liquidation around.
/// Not thread-safe: use it from one thread.
/// </summary>
public sealed class LiquidationTally
{
    private readonly Dictionary<(long Minute, string Symbol), (decimal Longs, decimal Shorts)> _minutes = [];

    public void Add(RadarEvent e)
    {
        if (e.IsWhale)
            return;
        var key = (e.Time.ToUnixTimeSeconds() / 60, e.Symbol);
        var (longs, shorts) = _minutes.GetValueOrDefault(key);
        _minutes[key] = e.Kind == RadarKind.LongLiquidated ? (longs + e.ValueUsd, shorts) : (longs, shorts + e.ValueUsd);
    }

    /// <summary>Longs and shorts liquidated in the hour before <paramref name="now"/>.</summary>
    /// <param name="include">Which coins count; all when null.</param>
    public (decimal Longs, decimal Shorts) LastHour(DateTimeOffset now, Func<string, bool>? include = null)
    {
        var first = now.ToUnixTimeSeconds() / 60 - 59;
        foreach (var old in _minutes.Keys.Where(k => k.Minute < first).ToList())
            _minutes.Remove(old);
        decimal longs = 0, shorts = 0;
        foreach (var ((_, symbol), sums) in _minutes)
        {
            if (include is null || include(symbol))
            {
                longs += sums.Longs;
                shorts += sums.Shorts;
            }
        }
        return (longs, shorts);
    }
}
