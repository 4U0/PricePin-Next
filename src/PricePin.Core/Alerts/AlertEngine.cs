namespace PricePin.Core.Alerts;

/// <summary>
/// Decides when alerts fire. An alert fires when its condition is met while it is "armed".
/// One-off alerts then switch off. Repeating alerts disarm and re-arm only once the price
/// has moved back past the line by a small margin (so a price hovering at the target doesn't
/// fire over and over), and never fire more often than <see cref="RepeatCooldown"/>.
/// </summary>
public sealed class AlertEngine
{
    public static readonly TimeSpan RepeatCooldown = TimeSpan.FromMinutes(5);

    /// <summary>How far back past a price target re-arms a repeating alert (0.25%).</summary>
    public const decimal PriceRearmMargin = 0.0025m;

    /// <summary>How far back past a percentage target re-arms it, in percentage points.</summary>
    public const decimal PercentRearmMargin = 0.5m;

    private readonly HashSet<string> _disarmed = [];

    /// <summary>Makes an alert fire on the next match again, e.g. after the user re-enables it.</summary>
    public void Rearm(string alertId) => _disarmed.Remove(alertId);

    /// <summary>
    /// Checks the alerts for one coin against a new price. Alerts that fire are updated in place
    /// (<see cref="PriceAlert.LastTriggered"/>, and <see cref="PriceAlert.Enabled"/> for one-off alerts).
    /// </summary>
    public List<AlertHit> Evaluate(IEnumerable<PriceAlert> alerts, string quote, string symbol,
                                   decimal price, decimal? changePercent, DateTimeOffset now)
    {
        var hits = new List<AlertHit>();
        foreach (var alert in alerts)
        {
            if (!alert.Enabled || !alert.IsValid
                || !string.Equals(alert.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(alert.Quote, quote, StringComparison.OrdinalIgnoreCase))
                continue;

            var met = IsMet(alert, price, changePercent);
            if (met is null)
                continue; // a percent alert without 24h data yet

            if (_disarmed.Contains(alert.Id))
            {
                if (IsRearmed(alert, price, changePercent))
                    _disarmed.Remove(alert.Id);
                continue;
            }

            if (met != true)
                continue;
            if (alert.Repeat && alert.LastTriggered is { } last && now - last < RepeatCooldown)
                continue;

            alert.LastTriggered = now;
            if (alert.Repeat)
                _disarmed.Add(alert.Id);
            else
                alert.Enabled = false;
            hits.Add(new AlertHit(alert, price, changePercent));
        }
        return hits;
    }

    private static bool? IsMet(PriceAlert a, decimal price, decimal? change) => a.Condition switch
    {
        AlertCondition.Above => price >= a.Value,
        AlertCondition.Below => price <= a.Value,
        AlertCondition.RisesBy => change is { } c ? c >= a.Value : null,
        AlertCondition.FallsBy => change is { } c ? c <= -a.Value : null,
        _ => false,
    };

    private static bool IsRearmed(PriceAlert a, decimal price, decimal? change) => a.Condition switch
    {
        AlertCondition.Above => price < a.Value * (1 - PriceRearmMargin),
        AlertCondition.Below => price > a.Value * (1 + PriceRearmMargin),
        AlertCondition.RisesBy => change is { } c && c < a.Value - PercentRearmMargin,
        AlertCondition.FallsBy => change is { } c && c > -a.Value + PercentRearmMargin,
        _ => false,
    };
}
