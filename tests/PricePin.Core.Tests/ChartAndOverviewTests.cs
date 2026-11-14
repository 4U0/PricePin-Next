using System.Text.Json;
using PricePin.Core.History;
using PricePin.Core.Market;

namespace PricePin.Core.Tests;

public class ChartAndOverviewTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Parses_binance_klines()
    {
        var c = CandleService.ParseBinance(J("""[[1700000000000,"1.0","2.0","0.5","1.5","100",1700000899999,"0",1,"0","0","0"]]"""));
        var k = Assert.Single(c);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), k.Time);
        Assert.Equal((1.0m, 2.0m, 0.5m, 1.5m, 100m), (k.Open, k.High, k.Low, k.Close, k.Volume));
    }

    [Fact]
    public void Parses_coinbase_candles_newest_first_into_oldest_first()
    {
        // [time, low, high, open, close, volume]
        var c = CandleService.ParseCoinbase(J("[[200, 1, 4, 2, 3, 10], [100, 0.5, 2, 1, 2, 5]]"));
        Assert.Equal(2, c.Count);
        Assert.Equal(100, c[0].Time.ToUnixTimeSeconds());
        Assert.Equal((2m, 4m, 1m, 3m, 10m), (c[1].Open, c[1].High, c[1].Low, c[1].Close, c[1].Volume));
    }

    [Fact]
    public void Parses_kraken_ohlc()
    {
        var c = CandleService.ParseKraken(J("""{"error":[],"result":{"XXBTZUSD":[[100,"1","3","0.5","2","1.8","42",7]],"last":100}}"""));
        var k = Assert.Single(c!);
        Assert.Equal((1m, 3m, 0.5m, 2m, 42m), (k.Open, k.High, k.Low, k.Close, k.Volume));
    }

    [Fact]
    public void Parses_market_overview()
    {
        var global = J("""
            {"data":{"total_market_cap":{"usd":3.2e12,"eur":2.9e12},
                     "market_cap_percentage":{"btc":57.4,"eth":11.2},
                     "market_cap_change_percentage_24h_usd":-1.25}}
            """);
        var fng = J("""{"name":"Fear and Greed Index","data":[{"value":"72","value_classification":"Greed"}]}""");
        var o = MarketOverviewService.Parse(global, fng, "eur", DateTimeOffset.UnixEpoch);
        Assert.Equal(2.9e12m, o.TotalMarketCap);
        Assert.Equal("eur", o.MarketCapCurrency);
        Assert.Equal(-1.25m, o.MarketCapChange24h);
        Assert.Equal(57.4m, o.BtcDominance);
        Assert.Equal(11.2m, o.EthDominance);
        Assert.Equal(72, o.FearGreed);
        Assert.Equal(FearGreedLevel.Greed, o.FearGreedLevel);
    }

    [Fact]
    public void Overview_falls_back_to_usd_and_survives_missing_sources()
    {
        var global = J("""{"data":{"total_market_cap":{"usd":100}}}""");
        var o = MarketOverviewService.Parse(global, null, "try", DateTimeOffset.UnixEpoch);
        Assert.Equal(100m, o.TotalMarketCap);
        Assert.Equal("usd", o.MarketCapCurrency);
        Assert.Null(o.FearGreed);
        Assert.True(MarketOverviewService.Parse(null, null, "usd", DateTimeOffset.UnixEpoch).IsEmpty);
    }

    [Theory]
    [InlineData(10, FearGreedLevel.ExtremeFear)]
    [InlineData(30, FearGreedLevel.Fear)]
    [InlineData(50, FearGreedLevel.Neutral)]
    [InlineData(60, FearGreedLevel.Greed)]
    [InlineData(90, FearGreedLevel.ExtremeGreed)]
    public void Fear_greed_bands(int value, FearGreedLevel level) =>
        Assert.Equal(level, new MarketOverview(null, "usd", null, null, null, value, default).FearGreedLevel);
}
