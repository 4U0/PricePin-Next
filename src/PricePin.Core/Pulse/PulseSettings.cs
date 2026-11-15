namespace PricePin.Core.Pulse;

/// <summary>Settings for news, the whale and liquidation radar, and network fees.</summary>
public sealed class PulseSettings
{
    // ---- News ----

    /// <summary>Show only headlines that mention a coin on the user's widgets or in their portfolio.</summary>
    public bool NewsOnlyMyCoins { get; set; }

    /// <summary>Notify about new headlines that mention a coin the user holds.</summary>
    public bool NotifyHeldCoinNews { get; set; }

    // ---- Radar ----

    /// <summary>Smallest spot trade that counts as a whale, in US dollars.</summary>
    public decimal MinWhaleUsd { get; set; } = 500_000m;

    /// <summary>Smallest liquidation shown, in US dollars.</summary>
    public decimal MinLiquidationUsd { get; set; } = 100_000m;

    /// <summary>Show only events for coins on the user's widgets or in their portfolio.</summary>
    public bool RadarOnlyMyCoins { get; set; } = true;

    /// <summary>Notify about very large events (at or above <see cref="NotifyRadarUsd"/>).</summary>
    public bool NotifyRadar { get; set; }

    public decimal NotifyRadarUsd { get; set; } = 5_000_000m;

    // ---- Fees ----

    /// <summary>Notify when Ethereum gas falls to this many gwei or less. Null is off.</summary>
    public decimal? EthGasBelowGwei { get; set; }

    /// <summary>Notify when the fast Bitcoin fee falls to this many sat/vB or less. Null is off.</summary>
    public decimal? BtcFeeBelowSatVb { get; set; }

    public static IReadOnlyList<decimal> WhaleThresholds { get; } = [100_000m, 250_000m, 500_000m, 1_000_000m, 5_000_000m];
    public static IReadOnlyList<decimal> LiquidationThresholds { get; } = [10_000m, 50_000m, 100_000m, 500_000m, 1_000_000m];
    public static IReadOnlyList<decimal> NotifyThresholds { get; } = [1_000_000m, 5_000_000m, 10_000_000m, 25_000_000m];

    public void Normalize()
    {
        MinWhaleUsd = Clamp(MinWhaleUsd, 10_000m, 100_000_000m, 500_000m);
        MinLiquidationUsd = Clamp(MinLiquidationUsd, 1_000m, 100_000_000m, 100_000m);
        NotifyRadarUsd = Clamp(NotifyRadarUsd, 100_000m, 1_000_000_000m, 5_000_000m);
        if (EthGasBelowGwei is { } g && (g <= 0 || g > 10_000)) EthGasBelowGwei = null;
        if (BtcFeeBelowSatVb is { } b && (b <= 0 || b > 10_000)) BtcFeeBelowSatVb = null;
    }

    private static decimal Clamp(decimal v, decimal min, decimal max, decimal fallback) =>
        v <= 0 ? fallback : Math.Clamp(v, min, max);
}

/// <summary>
/// A "fee fell below X" alert. It fires once when the fee reaches the level, then waits until
/// the fee has risen 20% above the level before it can fire again, so a fee hovering around
/// the line does not notify every minute.
/// </summary>
public sealed class FeeAlarm
{
    public const decimal RearmFactor = 1.2m;

    private bool _armed = true;

    /// <returns>True when the alert should fire for this reading.</returns>
    public bool Check(decimal? level, decimal? fee)
    {
        if (level is not { } l || fee is not { } f)
            return false;
        if (f <= l)
        {
            if (!_armed)
                return false;
            _armed = false;
            return true;
        }
        if (f >= l * RearmFactor)
            _armed = true;
        return false;
    }

    public void Reset() => _armed = true;
}
