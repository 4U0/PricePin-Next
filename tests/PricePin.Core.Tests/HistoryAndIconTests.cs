using System.Text.Json;
using PricePin.Core.History;
using PricePin.Core.Icons;

namespace PricePin.Core.Tests;

public class HistoryAndIconTests
{
    [Fact]
    public void Binance_klines_use_close_oldest_first()
    {
        var json = JsonDocument.Parse("""[[1,"1","2","0.5","1.5","10",2],[3,"1.5","3","1","2.5","10",4]]""").RootElement;
        Assert.Equal([1.5m, 2.5m], PriceHistoryService.ParseBinanceKlines(json));
    }

    [Fact]
    public void Coinbase_candles_are_reversed_to_oldest_first()
    {
        var json = JsonDocument.Parse("[[200,1,3,2,2.5,9],[100,1,2,1,1.5,9]]").RootElement;
        Assert.Equal([1.5m, 2.5m], PriceHistoryService.ParseCoinbaseCandles(json));
    }

    [Fact]
    public void Kraken_ohlc_reads_first_pair()
    {
        var json = JsonDocument.Parse("""
            {"error":[],"result":{"XXBTZUSD":[[1,"1","2","0.5","1.5","1","10",3],[2,"1.5","3","1","2.5","2","10",3]],"last":2}}
            """).RootElement;
        Assert.Equal([1.5m, 2.5m], PriceHistoryService.ParseKrakenOhlc(json));
    }

    [Fact]
    public void CoinGecko_picks_exact_symbol_with_best_rank()
    {
        var json = JsonDocument.Parse("""
            {"coins":[
              {"symbol":"SUIX","market_cap_rank":5,"large":"https://x/wrong.png"},
              {"symbol":"SUI","market_cap_rank":900,"large":"https://x/fake.png"},
              {"symbol":"SUI","market_cap_rank":20,"large":"https://x/sui.png"}]}
            """).RootElement;
        Assert.Equal("https://x/sui.png", CoinIconService.PickCoinGeckoImage(json, "sui"));
    }
}
