using System.Text.Json;
using PricePin.Core.Coins;
using PricePin.Core.Dex;
using PricePin.Core.Market;
using PricePin.Core.Settings;

namespace PricePin.Core.Tests;

public class DexTokenTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    private const string Bonk = "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263";
    private const string Pepe = "0x6982508145454Ce325dDbE47a25d4ec3d2311933";

    // Trimmed DexScreener pair objects, as /latest/dex/search and /tokens/v1 return them.
    private static string Pair(string chain, string pair, string address, string symbol, string price, decimal liquidity, string quote = "SOL") => $$"""
        {
          "chainId": "{{chain}}", "dexId": "raydium", "url": "https://dexscreener.com/{{chain}}/{{pair}}",
          "pairAddress": "{{pair}}",
          "baseToken": { "address": "{{address}}", "name": "{{symbol}} Token", "symbol": "{{symbol}}" },
          "quoteToken": { "address": "So11111111111111111111111111111111111111112", "name": "Wrapped SOL", "symbol": "{{quote}}" },
          "priceNative": "0.0000001", "priceUsd": "{{price}}",
          "volume": { "h24": 1000000, "h6": 1, "h1": 1, "m5": 1 },
          "priceChange": { "m5": 0.1, "h1": -1.5, "h6": 2, "h24": 25 },
          "liquidity": { "usd": {{liquidity}}, "base": 1, "quote": 1 },
          "fdv": 2000000000, "marketCap": 1500000000, "pairCreatedAt": 1700000000000,
          "info": { "imageUrl": "https://dd.dexscreener.com/ds-data/tokens/{{chain}}/{{address}}.png" }
        }
        """;

    [Fact]
    public void Keys_keep_case_sensitive_addresses_and_lower_case_evm_ones()
    {
        Assert.Equal($"dex:solana:{Bonk}", CoinKey.Normalize($"DEX:Solana:{Bonk}"));
        Assert.Equal($"dex:ethereum:{Pepe.ToLowerInvariant()}", CoinKey.ForDex("ethereum", Pepe));
        Assert.Equal("BTC", CoinKey.Normalize(" btc "));

        Assert.True(CoinKey.TryParseDex($"dex:solana:{Bonk}", out var chain, out var address));
        Assert.Equal(("solana", Bonk), (chain, address));
        // Sui coin types contain "::" and stay in one piece.
        Assert.True(CoinKey.TryParseDex("dex:sui:0x2::sui::SUI", out _, out var sui));
        Assert.Equal("0x2::sui::SUI", sui);
        Assert.False(CoinKey.TryParseDex("BTC", out _, out _));
    }

    [Theory]
    [InlineData(Bonk, true)]
    [InlineData(Pepe, true)]
    [InlineData("0x2::sui::SUI", true)]
    [InlineData("bonk", false)]
    [InlineData("Bitcoin", false)]
    public void Recognises_contract_addresses(string text, bool expected) =>
        Assert.Equal(expected, CoinKey.LooksLikeAddress(text));

    [Fact]
    public void Parses_pairs_and_keeps_the_most_liquid_pool_per_token()
    {
        var json = J($"[{Pair("solana", "P1", Bonk, "Bonk", "0.00002", 900_000)},{Pair("solana", "P2", Bonk, "Bonk", "0.000021", 4_000_000)}," +
                     $"{Pair("solana", "P3", "Scam111111111111111111111111111111111111", "Bonk", "1", 100)}]");
        var pairs = DexScreenerClient.ParsePairs(json);
        Assert.Equal(3, pairs.Count);
        var first = pairs[0];
        Assert.Equal(0.00002m, first.PriceUsd);
        Assert.Equal((-1.5m, 25m), (first.Change1h!.Value, first.Change24h!.Value));
        Assert.Equal(1_500_000_000m, first.MarketCapUsd);
        Assert.Equal("SOL", first.QuoteSymbol);
        Assert.StartsWith("https://", first.ImageUrl);

        var best = DexScreenerClient.BestPerToken(pairs, DexScreenerClient.MinSearchLiquidityUsd);
        var only = Assert.Single(best); // the $100 pool is dropped
        Assert.Equal("P2", only.PairAddress);
    }

    [Fact]
    public void Lists_tokens_that_trade_before_stale_look_alikes()
    {
        var stale = Pair("solana", "P9", "Fake11111111111111111111111111111111111111", "Bonk", "0.0000066", 1_700_000)
            .Replace("\"priceChange\": { \"m5\": 0.1, \"h1\": -1.5, \"h6\": 2, \"h24\": 25 },", "")
            .Replace("\"h24\": 1000000,", "");
        var pairs = DexScreenerClient.ParsePairs(J($"[{stale},{Pair("solana", "P2", Bonk, "Bonk", "0.0000022", 220_000)}]"));
        var best = DexScreenerClient.BestPerToken(pairs, DexScreenerClient.MinSearchLiquidityUsd);
        Assert.Equal(["P2", "P9"], best.Select(p => p.PairAddress));
    }

    [Fact]
    public void Prices_from_the_saved_pool_or_else_the_most_liquid_one()
    {
        var pairs = DexScreenerClient.ParsePairs(J($"[{Pair("solana", "P1", Bonk, "Bonk", "0.00002", 900_000)},{Pair("solana", "P2", Bonk, "Bonk", "0.000021", 4_000_000)}]"));
        var saved = new DexToken { Chain = "solana", Address = Bonk, Symbol = "Bonk", PairAddress = "P1" };
        Assert.Equal("P1", DexScreenerClient.PickPairs([saved], pairs)[saved.Key].PairAddress);

        var gone = new DexToken { Chain = "solana", Address = Bonk, Symbol = "Bonk", PairAddress = "Removed" };
        Assert.Equal("P2", DexScreenerClient.PickPairs([gone], pairs)[gone.Key].PairAddress);
    }

    [Fact]
    public void Converts_dollar_prices_to_the_display_currency()
    {
        var pair = DexScreenerClient.ParsePairs(J($"[{Pair("solana", "P1", Bonk, "Bonk", "0.00002", 900_000)}]"))[0];
        var t = DexPriceService.ToTicker(pair.Key, pair, usdToQuote: 40m); // e.g. TRY

        Assert.Equal(pair.Key, t.Base);
        Assert.Equal(0.0008m, t.Last);
        Assert.Equal(25m, t.ChangePercent24h);
        Assert.Equal(-1.5m, t.ChangePercent1h);
        Assert.Equal(60_000_000_000m, t.MarketCap);
        // The open is worked back from the 24h change so the row's own maths agrees with it.
        Assert.Equal(25m, Math.Round((t.Last - t.Open24h!.Value) / t.Open24h.Value * 100m, 6));
        Assert.Equal(1_000_000m / 0.00002m, t.Volume24h); // dollars of volume become tokens
    }

    [Fact]
    public void Parses_geckoterminal_candles_oldest_first()
    {
        var candles = GeckoTerminalClient.ParseOhlcv(J("""
            {"data":{"id":"x","type":"ohlcv_request_response","attributes":{"ohlcv_list":[
              [1700003600, 2.0, 2.5, 1.9, 2.4, 1200.5],
              [1700000000, 1.0, 2.1, 0.9, 2.0, 600]
            ]}},"meta":{}}
            """))!;
        Assert.Equal(2, candles.Count);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), candles[0].Time);
        Assert.Equal(2.4m, candles[1].Close);

        var inEuro = GeckoTerminalClient.Convert(candles, 0.5m);
        Assert.Equal(1.2m, inEuro[1].Close);
        Assert.Equal(1200.5m / 2.4m, inEuro[1].Volume); // dollars of volume become tokens
    }

    [Fact]
    public void Parses_fiat_rates_from_both_sources()
    {
        var coinbase = FiatRates.ParseCoinbase(J("""{"data":{"currency":"USD","rates":{"EUR":"0.92","TRY":"34.1","BAD":"x"}}}"""))!;
        Assert.Equal(0.92m, coinbase["EUR"]);
        Assert.Equal(34.1m, coinbase["try"]);
        Assert.False(coinbase.ContainsKey("BAD"));

        var openEr = FiatRates.ParseOpenEr(J("""{"result":"success","rates":{"GBP":0.79}}"""))!;
        Assert.Equal(0.79m, openEr["GBP"]);
    }

    [Fact]
    public void Settings_keep_dex_keys_intact_and_register_tokens()
    {
        var s = new AppSettings
        {
            Widgets = [new WidgetSettings { Coins = ["btc", $"dex:Solana:{Bonk}", $"dex:solana:{Bonk}"] }],
            Tokens = [new DexToken { Chain = "Solana", Address = Bonk, Symbol = " Bonk ", Name = "", PairAddress = "P1", ImageUrl = "http://insecure" }],
            Holdings = [new Core.Portfolio.Holding { Symbol = $"dex:solana:{Bonk}", Amount = 1_000_000 }],
            TrayCoin = $"dex:solana:{Bonk}",
        };
        s.Normalize();

        Assert.Equal(["BTC", $"dex:solana:{Bonk}"], s.Widgets[0].Coins);
        Assert.Equal($"dex:solana:{Bonk}", s.Holdings[0].Symbol);
        Assert.Equal($"dex:solana:{Bonk}", s.TrayCoin);
        var token = Assert.Single(s.Tokens);
        Assert.Equal(("solana", "Bonk", "Bonk"), (token.Chain, token.Symbol, token.Name));
        Assert.Null(token.ImageUrl);

        Assert.Equal("Bonk", CoinCatalog.DisplaySymbol($"dex:solana:{Bonk}"));
        Assert.Equal("Bonk", CoinCatalog.Get($"dex:solana:{Bonk}").Name);
    }

    [Fact]
    public void Unknown_dex_keys_show_a_short_address()
    {
        var info = CoinCatalog.Get("dex:base:0x0000000000000000000000000000000000000abc");
        Assert.Equal("0x00…0abc", info.Symbol);
        Assert.Equal("Base", info.Name);
    }

    [Fact]
    public void Typed_symbols_resolve_to_dex_keys_the_user_already_has()
    {
        var wif = new DexToken { Chain = "solana", Address = "EKpQGSJtjMFqKZ9KQanSqYXRcF8fBopzLHYxdM65zcjm", Symbol = "WIF", Name = "dogwifhat" };
        CoinCatalog.Register(wif);

        Assert.Equal(wif.Key, CoinCatalog.ResolveTyped("wif", ["BTC", wif.Key]));
        Assert.Equal("BTC", CoinCatalog.ResolveTyped("btc", ["BTC", wif.Key]));
        Assert.Equal("NEWCOIN", CoinCatalog.ResolveTyped("newcoin", ["BTC"]));
        Assert.True(CoinCatalog.IsValidKey(wif.Key));
        Assert.False(CoinCatalog.IsValidKey("B"));
    }

    [Fact]
    public void Widgets_default_to_the_24h_change()
    {
        var w = new WidgetSettings { ChangePeriod = (ChangePeriod)42 };
        w.Normalize();
        Assert.Equal(ChangePeriod.Day, w.ChangePeriod);
    }
}
