using PricePin.Core.Alerts;
using PricePin.Core.Settings;

namespace PricePin.Core.Tests;

public class AlertEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static PriceAlert Alert(AlertCondition c, decimal value, bool repeat = false) =>
        new() { Symbol = "BTC", Quote = "USD", Condition = c, Value = value, Repeat = repeat };

    [Fact]
    public void Above_fires_once_and_switches_off()
    {
        var engine = new AlertEngine();
        var alert = Alert(AlertCondition.Above, 70_000m);
        PriceAlert[] alerts = [alert];

        Assert.Empty(engine.Evaluate(alerts, "USD", "BTC", 69_999m, null, T0));
        var hit = Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 70_010m, null, T0));
        Assert.Equal(70_010m, hit.Price);
        Assert.False(alert.Enabled);
        Assert.Equal(T0, alert.LastTriggered);
        Assert.Empty(engine.Evaluate(alerts, "USD", "BTC", 71_000m, null, T0.AddMinutes(10)));
    }

    [Fact]
    public void Below_fires_when_price_reaches_target()
    {
        var engine = new AlertEngine();
        PriceAlert[] alerts = [Alert(AlertCondition.Below, 60_000m)];
        Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 60_000m, null, T0));
    }

    [Fact]
    public void Repeating_alert_needs_price_to_move_back_before_firing_again()
    {
        var engine = new AlertEngine();
        var alert = Alert(AlertCondition.Above, 100m, repeat: true);
        PriceAlert[] alerts = [alert];

        Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 100.5m, null, T0));
        Assert.True(alert.Enabled);
        // Hovering around the line doesn't re-fire, even after the cooldown.
        Assert.Empty(engine.Evaluate(alerts, "USD", "BTC", 99.9m, null, T0.AddMinutes(10)));
        Assert.Empty(engine.Evaluate(alerts, "USD", "BTC", 100.2m, null, T0.AddMinutes(11)));
        // Dropping clearly below re-arms; crossing again fires.
        Assert.Empty(engine.Evaluate(alerts, "USD", "BTC", 99m, null, T0.AddMinutes(12)));
        Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 101m, null, T0.AddMinutes(13)));
    }

    [Fact]
    public void Repeating_alert_respects_cooldown()
    {
        var engine = new AlertEngine();
        var alert = Alert(AlertCondition.Above, 100m, repeat: true);
        PriceAlert[] alerts = [alert];

        Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 101m, null, T0));
        Assert.Empty(engine.Evaluate(alerts, "USD", "BTC", 90m, null, T0.AddMinutes(1)));   // re-armed
        Assert.Empty(engine.Evaluate(alerts, "USD", "BTC", 101m, null, T0.AddMinutes(2)));  // too soon
        Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 101m, null, T0.AddMinutes(6)));
    }

    [Fact]
    public void Percent_alerts_use_24h_change_and_wait_for_data()
    {
        var engine = new AlertEngine();
        PriceAlert[] alerts = [Alert(AlertCondition.RisesBy, 5m), Alert(AlertCondition.FallsBy, 5m)];

        Assert.Empty(engine.Evaluate(alerts, "USD", "BTC", 100m, null, T0));
        Assert.Empty(engine.Evaluate(alerts, "USD", "BTC", 100m, 4.9m, T0));
        var up = Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 100m, 5.1m, T0));
        Assert.Equal(AlertCondition.RisesBy, up.Alert.Condition);
        var down = Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 100m, -6m, T0));
        Assert.Equal(AlertCondition.FallsBy, down.Alert.Condition);
    }

    [Fact]
    public void Ignores_other_coins_other_currencies_and_disabled_alerts()
    {
        var engine = new AlertEngine();
        var off = Alert(AlertCondition.Above, 1m);
        off.Enabled = false;
        var eur = Alert(AlertCondition.Above, 1m);
        eur.Quote = "EUR";
        PriceAlert[] alerts = [off, eur, Alert(AlertCondition.Above, 1m)];

        Assert.Empty(engine.Evaluate(alerts, "USD", "ETH", 5m, null, T0));
        Assert.Single(engine.Evaluate(alerts, "usd", "btc", 5m, null, T0));
    }

    [Fact]
    public void Rearm_lets_a_repeating_alert_fire_again()
    {
        var engine = new AlertEngine();
        var alert = Alert(AlertCondition.Above, 100m, repeat: true);
        PriceAlert[] alerts = [alert];
        Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 101m, null, T0));
        engine.Rearm(alert.Id);
        Assert.Single(engine.Evaluate(alerts, "USD", "BTC", 101m, null, T0.AddMinutes(6)));
    }

    [Fact]
    public void Settings_drop_invalid_alerts()
    {
        var s = new AppSettings
        {
            Alerts =
            [
                new PriceAlert { Symbol = " btc ", Quote = "usd", Value = 1 },
                new PriceAlert { Symbol = "", Value = 1 },
                new PriceAlert { Symbol = "ETH", Value = 0 },
            ],
        };
        s.Normalize();
        var only = Assert.Single(s.Alerts);
        Assert.Equal("BTC", only.Symbol);
        Assert.Equal("USD", only.Quote);
    }
}
