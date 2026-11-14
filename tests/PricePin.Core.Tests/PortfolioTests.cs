using PricePin.Core.Portfolio;
using PricePin.Core.Settings;

namespace PricePin.Core.Tests;

public class PortfolioTests
{
    [Fact]
    public void Line_values_change_and_profit()
    {
        // 2 coins at 110, up 10% today (from 100), bought at 50.
        var line = new PortfolioLine("X", 2m, 110m, 10m, 50m);
        Assert.Equal(220m, line.Value);
        Assert.Equal(20m, line.Change24h);
        Assert.Equal(100m, line.Cost);
        Assert.Equal(120m, line.ProfitLoss);
        Assert.Equal(120m, line.ProfitLossPercent);
    }

    [Fact]
    public void Totals_cover_what_can_be_worked_out()
    {
        PortfolioLine[] lines =
        [
            new("A", 1m, 110m, 10m, 100m),   // value 110, +10 today, +10 P/L
            new("B", 10m, 9m, -10m, null),   // value 90, -10 today, no cost
            new("C", 5m, null, null, 1m),    // no price yet
        ];
        var t = PortfolioMath.Totals(lines);
        Assert.Equal(200m, t.Value);
        Assert.Equal(0m, t.Change24h);
        Assert.Equal(0m, t.ChangePercent24h);
        Assert.Equal(100m, t.Cost);
        Assert.Equal(10m, t.ProfitLoss);
        Assert.Equal(10m, t.ProfitLossPercent);
        Assert.Equal(1, t.Unpriced);
    }

    [Fact]
    public void Totals_of_nothing_are_zero()
    {
        var t = PortfolioMath.Totals([]);
        Assert.Equal(0m, t.Value);
        Assert.Null(t.Change24h);
        Assert.Null(t.ProfitLoss);
    }

    [Fact]
    public void Settings_merge_duplicate_holdings_with_weighted_cost()
    {
        var s = new AppSettings
        {
            Holdings =
            [
                new Holding { Symbol = "btc", Amount = 1m, AverageCost = 100m },
                new Holding { Symbol = "BTC", Amount = 3m, AverageCost = 200m },
                new Holding { Symbol = "ETH", Amount = 0m },
            ],
        };
        s.Normalize();
        var btc = Assert.Single(s.Holdings);
        Assert.Equal("BTC", btc.Symbol);
        Assert.Equal(4m, btc.Amount);
        Assert.Equal(175m, btc.AverageCost);
    }
}
