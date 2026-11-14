using System.Text;
using System.Text.Json;
using PricePin.Core.Feeds;
using PricePin.Core.Market;

namespace PricePin.Core.Tests;

public class FeedParsingTests
{
    private static readonly MarketSymbol[] Symbols = [new("BTC", "USD"), new("ETH", "USD")];

    private static List<Ticker> Parse(WebSocketFeedBase feed, string json)
    {
        var list = new List<Ticker>();
        feed.ParseMessage(Encoding.UTF8.GetBytes(json), list.Add);
        return list;
    }

    [Fact]
    public void Binance_maps_usd_to_usdt()
    {
        Assert.Equal("btcusdt", BinanceFeed.ToExchangeSymbol(new MarketSymbol("btc", "usd")));
        Assert.Equal("btceur", BinanceFeed.ToExchangeSymbol(new MarketSymbol("BTC", "EUR")));
    }

    [Fact]
    public void Binance_reads_agg_trade()
    {
        var feed = new BinanceFeed();
        feed.PrepareForTest(Symbols);

        var t = Assert.Single(Parse(feed, """
            {"stream":"btcusdt@aggTrade","data":{"e":"aggTrade","E":1731590000123,"s":"BTCUSDT","a":1,"p":"97412.35000000","q":"0.010","T":1731590000120,"m":false}}
            """));

        Assert.Equal("BTC", t.Base);
        Assert.Equal(97412.35m, t.Last);
        Assert.Null(t.ChangePercent24h);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1731590000120), t.ExchangeTime);
        Assert.Equal("Binance", t.Source);
    }

    [Fact]
    public void Binance_reads_24h_ticker()
    {
        var feed = new BinanceFeed();
        feed.PrepareForTest(Symbols);

        var t = Assert.Single(Parse(feed, """
            {"stream":"ethusdt@ticker","data":{"e":"24hrTicker","E":1731590000000,"s":"ETHUSDT","p":"-28.80","P":"-0.820","o":"3509.87","h":"3550.00","l":"3400.10","c":"3481.07","v":"123456.7"}}
            """));

        Assert.Equal("ETH", t.Base);
        Assert.Equal(3481.07m, t.Last);
        Assert.Equal(3509.87m, t.Open24h);
        Assert.Equal(3550.00m, t.High24h);
        Assert.Equal(3400.10m, t.Low24h);
        Assert.Equal(-0.820m, t.ChangePercent24h);
    }

    [Theory]
    [InlineData("""{"result":null,"id":1}""")]
    [InlineData("""{"stream":"dogeusdt@aggTrade","data":{"e":"aggTrade","s":"DOGEUSDT","p":"0.38"}}""")]
    public void Binance_ignores_acks_and_unsubscribed_symbols(string json)
    {
        var feed = new BinanceFeed();
        feed.PrepareForTest(Symbols);
        Assert.Empty(Parse(feed, json));
    }

    [Fact]
    public void Coinbase_reads_ticker_and_derives_open()
    {
        var feed = new CoinbaseFeed();
        feed.PrepareForTest(Symbols);

        var t = Assert.Single(Parse(feed, """
            {"channel":"ticker","client_id":"","timestamp":"2026-11-14T13:00:00.123456Z","sequence_num":5,
             "events":[{"type":"update","tickers":[{"type":"ticker","product_id":"BTC-USD","price":"101000","volume_24_h":"9000.5",
               "low_24_h":"98000","high_24_h":"102000","low_52_w":"1","high_52_w":"2","price_percent_chg_24_h":"1"}]}]}
            """));

        Assert.Equal("BTC", t.Base);
        Assert.Equal(101000m, t.Last);
        Assert.Equal(100000m, t.Open24h);
        Assert.Equal(1m, t.ChangePercent24h);
        Assert.Equal(98000m, t.Low24h);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 13, 0, 0, TimeSpan.Zero).AddTicks(1234560), t.ExchangeTime);
    }

    [Fact]
    public void Coinbase_ignores_heartbeats()
    {
        var feed = new CoinbaseFeed();
        feed.PrepareForTest(Symbols);
        Assert.Empty(Parse(feed, """{"channel":"heartbeats","events":[{"current_time":"x","heartbeat_counter":3}]}"""));
    }

    [Fact]
    public void Kraken_reads_ticker()
    {
        var feed = new KrakenFeed();
        feed.PrepareForTest(Symbols);

        var t = Assert.Single(Parse(feed, """
            {"channel":"ticker","type":"update","data":[{"symbol":"BTC/USD","bid":96999.9,"bid_qty":1.2,"ask":97000.1,"ask_qty":0.5,
              "last":97000.0,"volume":1234.5,"vwap":96500.2,"low":95000.0,"high":98000.0,"change":1000.0,"change_pct":1.04}]}
            """));

        Assert.Equal("BTC", t.Base);
        Assert.Equal(97000.0m, t.Last);
        Assert.Equal(96000.0m, t.Open24h);
        Assert.Equal(1.04m, t.ChangePercent24h);
    }

    [Theory]
    [InlineData("""{"channel":"heartbeat"}""")]
    [InlineData("""{"method":"subscribe","success":true,"result":{"channel":"ticker","symbol":"BTC/USD"}}""")]
    [InlineData("""{"channel":"status","type":"update","data":[{"system":"online"}]}""")]
    public void Kraken_ignores_control_messages(string json)
    {
        var feed = new KrakenFeed();
        feed.PrepareForTest(Symbols);
        Assert.Empty(Parse(feed, json));
    }

    [Fact]
    public void Binance_rest_reads_batch()
    {
        var json = JsonDocument.Parse("""
            [{"symbol":"BTCUSDT","priceChangePercent":"2.140","lastPrice":"97412.35","openPrice":"95371.40","highPrice":"98000","lowPrice":"95000","volume":"20000","closeTime":1731590000000}]
            """).RootElement;

        var t = Assert.Single(BinanceRestFeed.Parse(json, new Dictionary<string, string> { ["BTCUSDT"] = "BTC" }, "Binance (REST)"));
        Assert.Equal(97412.35m, t.Last);
        Assert.Equal(2.140m, t.ChangePercent24h);
    }

    [Fact]
    public void Coinbase_rest_computes_change()
    {
        var json = JsonDocument.Parse("""{"open":"100","high":"120","low":"90","last":"110","volume":"5"}""").RootElement;
        var t = CoinbaseRestFeed.Parse(json, "BTC", "Coinbase (REST)");
        Assert.NotNull(t);
        Assert.Equal(10m, t.ChangePercent24h);
    }
}
