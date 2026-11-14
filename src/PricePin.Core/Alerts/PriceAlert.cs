namespace PricePin.Core.Alerts;

public enum AlertCondition
{
    /// <summary>Price reaches or goes above <see cref="PriceAlert.Value"/>.</summary>
    Above,

    /// <summary>Price reaches or goes below <see cref="PriceAlert.Value"/>.</summary>
    Below,

    /// <summary>24h change reaches +<see cref="PriceAlert.Value"/> percent or more.</summary>
    RisesBy,

    /// <summary>24h change reaches −<see cref="PriceAlert.Value"/> percent or more.</summary>
    FallsBy,
}

/// <summary>A price alert, saved in the settings file.</summary>
public sealed class PriceAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Coin symbol, e.g. "BTC".</summary>
    public string Symbol { get; set; } = "";

    /// <summary>Currency the target price is in. The alert pauses while prices show in another currency.</summary>
    public string Quote { get; set; } = "USD";

    public AlertCondition Condition { get; set; }

    /// <summary>Target price, or a percentage for <see cref="AlertCondition.RisesBy"/> / <see cref="AlertCondition.FallsBy"/>.</summary>
    public decimal Value { get; set; }

    /// <summary>Fire again each time the condition is met anew; otherwise switch off after firing once.</summary>
    public bool Repeat { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset? LastTriggered { get; set; }

    public bool IsPercent => Condition is AlertCondition.RisesBy or AlertCondition.FallsBy;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Symbol) && !string.IsNullOrWhiteSpace(Quote) && Value > 0 && Enum.IsDefined(Condition);
}

/// <summary>An alert that just fired, with the price that set it off.</summary>
public sealed record AlertHit(PriceAlert Alert, decimal Price, decimal? ChangePercent);
