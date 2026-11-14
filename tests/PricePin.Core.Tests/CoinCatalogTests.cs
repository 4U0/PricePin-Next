using System.Text.Json;
using PricePin.Core.Coins;

namespace PricePin.Core.Tests;

public class CoinCatalogTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Binance_lists_bases_for_the_quote()
    {
        var json = J("""
            {"symbols":[
              {"symbol":"BTCUSDT","status":"TRADING","baseAsset":"BTC","quoteAsset":"USDT"},
              {"symbol":"ETHTRY","status":"TRADING","baseAsset":"ETH","quoteAsset":"TRY"},
              {"symbol":"LUNAUSDT","status":"BREAK","baseAsset":"LUNA","quoteAsset":"USDT"}]}
            """);
        Assert.Equal(["BTC"], CoinCatalogService.ParseBinance(json, "USDT"));
        Assert.Equal(["ETH"], CoinCatalogService.ParseBinance(json, "TRY"));
    }

    [Fact]
    public void Coinbase_skips_disabled_products()
    {
        var json = J("""
            [{"id":"BTC-USD","base_currency":"BTC","quote_currency":"USD","status":"online","trading_disabled":false},
             {"id":"OLD-USD","base_currency":"OLD","quote_currency":"USD","status":"delisted","trading_disabled":true},
             {"id":"ETH-EUR","base_currency":"ETH","quote_currency":"EUR","status":"online","trading_disabled":false}]
            """);
        Assert.Equal(["BTC"], CoinCatalogService.ParseCoinbase(json, "USD"));
    }

    [Fact]
    public void Kraken_translates_legacy_names()
    {
        var json = J("""
            {"error":[],"result":{
              "XXBTZUSD":{"altname":"XBTUSD","wsname":"XBT/USD","status":"online"},
              "XDGUSD":{"altname":"XDGUSD","wsname":"XDG/USD","status":"online"},
              "SOLEUR":{"altname":"SOLEUR","wsname":"SOL/EUR","status":"online"}}}
            """);
        var bases = CoinCatalogService.ParseKraken(json, "USD");
        Assert.Contains("BTC", bases);
        Assert.Contains("DOGE", bases);
        Assert.DoesNotContain("SOL", bases);
    }

    [Fact]
    public void Merge_ranks_popular_first_and_names_coins()
    {
        var coins = CoinCatalogService.Merge(
            [("Binance", ["ZZZ", "ETH", "BTC", "NEWCOIN"]), ("Coinbase", ["BTC", "NEWCOIN"])],
            new Dictionary<string, string> { ["NEWCOIN"] = "New Coin" });

        Assert.Equal(["BTC", "ETH", "NEWCOIN", "ZZZ"], coins.Select(c => c.Symbol));
        Assert.Equal(["Binance", "Coinbase"], coins[0].Exchanges);
        Assert.Equal("New Coin", coins[2].Name);
        Assert.Equal("ZZZ", coins[3].Name);
    }

    [Fact]
    public void Search_prefers_exact_symbol_then_prefix_then_name()
    {
        var coins = CoinCatalogService.Merge(
            [("Binance", ["SOL", "SOLO", "SUSHI", "BTC"])],
            new Dictionary<string, string> { ["SOLO"] = "Sologenic", ["SUSHI"] = "SushiSwap" });

        Assert.Equal(["SOL", "SOLO"], CoinCatalogService.Search(coins, "sol").Select(c => c.Symbol));
        Assert.Equal(["SUSHI"], CoinCatalogService.Search(coins, "swap").Select(c => c.Symbol));
        Assert.Equal(["BTC"], CoinCatalogService.Search(coins, "bitcoin").Select(c => c.Symbol));
    }
}
