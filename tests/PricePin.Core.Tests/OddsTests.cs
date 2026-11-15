using System.Text.Json;
using PricePin.Core.News;
using PricePin.Core.Odds;
using PricePin.Core.Pulse;

namespace PricePin.Core.Tests;

public class OddsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string Polymarket = """
    [
      {"id":"101","slug":"what-price-will-bitcoin-hit-in-december","title":"What price will Bitcoin hit in December?",
       "endDate":"2027-01-01T05:00:00Z","volume":2500000.5,"volume24hr":180000,"negRisk":false,
       "markets":[
         {"question":"Will Bitcoin reach $150,000 in December?","groupItemTitle":"↑ 150,000","outcomes":"[\"Yes\", \"No\"]","outcomePrices":"[\"0.04\", \"0.96\"]","oneDayPriceChange":-0.01,"active":true,"closed":false},
         {"question":"Will Bitcoin reach $120,000 in December?","groupItemTitle":"↑ 120,000","outcomes":"[\"Yes\", \"No\"]","outcomePrices":"[\"0.31\", \"0.69\"]","oneDayPriceChange":0.06,"active":true,"closed":false},
         {"question":"Will Bitcoin reach $110,000 in December?","groupItemTitle":"↑ 110,000","outcomes":"[\"Yes\", \"No\"]","outcomePrices":"[\"0.55\", \"0.45\"]","active":true,"closed":false},
         {"question":"Will Bitcoin dip to $80,000 in December?","groupItemTitle":"↓ 80,000","outcomes":"[\"Yes\", \"No\"]","outcomePrices":"[\"0.12\", \"0.88\"]","active":true,"closed":false},
         {"question":"Old one","groupItemTitle":"↑ 100,000","outcomes":"[\"Yes\", \"No\"]","outcomePrices":"[\"1\", \"0\"]","active":true,"closed":true}
       ]},
      {"id":"102","slug":"eth-etf-staking-approved","title":"ETH ETF staking approved in 2026?","endDate":"2026-12-31T00:00:00Z",
       "volume":90000,"volume24hr":12000,
       "markets":[{"question":"ETH ETF staking approved in 2026?","outcomes":"[\"Yes\",\"No\"]","outcomePrices":"[\"0.725\",\"0.275\"]","oneDayPriceChange":0.02,"active":true,"closed":false}]},
      {"id":"103","slug":"bitcoin-up-or-down-november-16-1pm-et","title":"Bitcoin Up or Down - November 16, 1PM ET","endDate":"2026-11-16T18:00:00Z",
       "volume24hr":999999999,"markets":[{"outcomes":"[\"Up\",\"Down\"]","outcomePrices":"[\"0.5\",\"0.5\"]"}]},
      {"id":"104","slug":"ends-soon","title":"Bitcoin above 95k at noon?","endDate":"2026-11-16T12:30:00Z","volume24hr":5,
       "markets":[{"outcomes":"[\"Yes\",\"No\"]","outcomePrices":"[\"0.9\",\"0.1\"]"}]},
      {"id":"105","slug":"which-chain-flips","title":"Which will be bigger at year end?","endDate":"2026-12-31T00:00:00Z","volume24hr":700,
       "markets":[{"outcomes":"[\"Solana\",\"BNB\"]","outcomePrices":"[\"0.4\",\"0.6\"]","oneDayPriceChange":-0.03}]},
      {"id":"106","slug":"bad slug/../x","title":"Bad link","markets":[]}
    ]
    """;

    [Fact]
    public void Polymarket_ReadsLaddersYesNoAndNamedSides_AndSkipsNoise()
    {
        var markets = OddsService.ParsePolymarket(Parse(Polymarket), Now)!;
        Assert.Equal(["pm:101", "pm:102", "pm:105"], markets.Select(m => m.Id));

        var ladder = markets[0];
        Assert.Equal("https://polymarket.com/event/what-price-will-bitcoin-hit-in-december", ladder.Url);
        Assert.Equal(180000m, ladder.Volume24hUsd);
        Assert.False(ladder.Exclusive);
        Assert.False(ladder.IsYesNo);
        Assert.Equal(4, ladder.Outcomes.Count); // the closed rung is gone
        Assert.Equal(0.06m, ladder.Outcomes[1].Change24h);

        var yesNo = markets[1];
        Assert.True(yesNo.IsYesNo);
        Assert.Equal(0.725m, yesNo.Lead.Chance);

        var sides = markets[2];
        Assert.True(sides.Exclusive);
        Assert.Equal(["Solana", "BNB"], sides.Outcomes.Select(o => o.Label));
        Assert.Equal("BNB", sides.Lead.Label);
    }

    [Fact]
    public void Highlights_PickFavouritesOrCoinFlips_InMarketOrder()
    {
        var ladder = OddsService.ParsePolymarket(Parse(Polymarket), Now)![0];
        // A ladder: the rungs closest to 50% (0.55, 0.31), still in the market's order.
        Assert.Equal(["↑ 120,000", "↑ 110,000"], ladder.Highlights(2).Select(o => o.Label));
        Assert.Equal("↑ 110,000", ladder.Lead.Label);

        var exclusive = ladder with { Exclusive = true };
        Assert.Equal(["↑ 120,000", "↑ 110,000"], exclusive.Highlights(2).Select(o => o.Label));
        Assert.Equal(["↑ 150,000", "↑ 120,000", "↑ 110,000", "↓ 80,000"], exclusive.Highlights(4).Select(o => o.Label));
        Assert.Equal("↑ 110,000", exclusive.Lead.Label);
    }

    private const string Kalshi = """
    {"events":[
      {"event_ticker":"KXBTCMAXY-26","series_ticker":"KXBTCMAXY","title":"How high will Bitcoin get this year?","mutually_exclusive":false,
       "markets":[
         {"ticker":"KXBTCMAXY-26-T200000","yes_sub_title":"$200,000 or above","status":"active","close_time":"2026-12-31T23:59:00Z","floor_strike":200000,
          "yes_bid_dollars":"0.0200","yes_ask_dollars":"0.0400","last_price_dollars":"0.0300","previous_price_dollars":"0.0300","volume_24h_fp":"1000.00","volume_fp":"50000.00"},
         {"ticker":"KXBTCMAXY-26-T150000","yes_sub_title":"$150,000 or above","status":"active","close_time":"2026-12-31T23:59:00Z","floor_strike":150000,
          "yes_bid":12,"yes_ask":14,"last_price":13,"previous_price":11,"volume_24h":5000,"volume":90000},
         {"ticker":"KXBTCMAXY-26-T125000","yes_sub_title":"$125,000 or above","status":"finalized","close_time":"2026-12-31T23:59:00Z","last_price_dollars":"1.00"}
       ]},
      {"event_ticker":"KXBTCD-26NOV1612","series_ticker":"KXBTCD","title":"Bitcoin price today at noon EST?","markets":[
         {"yes_sub_title":"$96,000 or above","status":"active","close_time":"2026-11-16T12:20:00Z","last_price_dollars":"0.5"}]},
      {"event_ticker":"X","series_ticker":"BAD/../","title":"Bad","markets":[{"yes_sub_title":"x","last_price_dollars":"0.5"}]}
    ],"cursor":""}
    """;

    [Fact]
    public void Kalshi_ReadsDollarAndCentFields_SortsByStrike_AndSkipsClosedOrEnding()
    {
        var markets = OddsService.ParseKalshi(Parse(Kalshi), Now)!;
        var m = Assert.Single(markets);
        Assert.Equal("ks:KXBTCMAXY-26", m.Id);
        Assert.Equal("https://kalshi.com/markets/kxbtcmaxy", m.Url);
        Assert.Equal("Kalshi", m.Source);
        Assert.Equal(["$150,000 or above", "$200,000 or above"], m.Outcomes.Select(o => o.Label));
        Assert.Equal(0.13m, m.Outcomes[0].Chance); // middle of 12¢ and 14¢
        Assert.Equal(0.02m, m.Outcomes[0].Change24h);
        Assert.Equal(0m, m.Outcomes[1].Change24h);
        Assert.Equal(Math.Round(5000 * 0.13m + 1000 * 0.03m), m.Volume24hUsd);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.Zero), m.Ends);
    }

    [Theory]
    [InlineData("Above $99,999.99", "Above $100,000")]
    [InlineData("Below $45,000.00", "Below $45,000")]
    [InlineData("$64,750 or above", "$64,750 or above")]
    [InlineData("$1,639.50 or above", "$1,639.50 or above")]
    [InlineData("Between $999.99 and $1,999.99", "Between $1,000 and $2,000")]
    public void KalshiStrikes_AreRounded(string raw, string expected) =>
        Assert.Equal(expected, OddsService.TidyAmounts(raw));

    [Fact]
    public void Parsers_RejectOtherShapes()
    {
        Assert.Null(OddsService.ParsePolymarket(Parse("{}"), Now));
        Assert.Null(OddsService.ParseKalshi(Parse("[]"), Now));
        Assert.Empty(OddsService.ParsePolymarket(Parse("""[{"title":"x","slug":"x","markets":[{"outcomePrices":"not json"}]}]"""), Now)!);
    }

    [Fact]
    public void Merge_SortsByVolume_AndTagsCoins()
    {
        var a = OddsService.ParsePolymarket(Parse(Polymarket), Now)!;
        var b = OddsService.ParseKalshi(Parse(Kalshi), Now)!;
        var merged = OddsService.Merge(a.Concat(b).Concat(a), [new NewsCoin("BTC", "BTC", "Bitcoin"), new NewsCoin("ETH", "ETH", "Ethereum")]);
        Assert.Equal(["pm:101", "pm:102", "pm:105", "ks:KXBTCMAXY-26"], merged.Select(m => m.Id));
        Assert.Equal(["BTC"], merged[0].Coins);
        Assert.Equal(["ETH"], merged[1].Coins);
        Assert.Empty(merged[2].Coins);
        Assert.Equal(["BTC"], merged[3].Coins);
    }

    [Fact]
    public void MoveWatcher_FiresOncePerTenPointSwing()
    {
        var watcher = new OddsMoveWatcher();
        OddsMarket At(decimal chance) => new("pm:1", "Q", "https://x", "Polymarket", null, 0, 0, [new OddsOutcome("Yes", chance, null)], false, []);

        Assert.Empty(watcher.Check(At(0.30m)));  // first look only remembers
        Assert.Empty(watcher.Check(At(0.38m)));
        var moved = Assert.Single(watcher.Check(At(0.41m)));
        Assert.Equal(0.30m, moved.From);
        Assert.Empty(watcher.Check(At(0.45m)));  // measured from 41% now
        Assert.Single(watcher.Check(At(0.30m)));

        watcher.Forget("pm:1");
        Assert.Empty(watcher.Check(At(0.90m)));
    }

    [Fact]
    public void PinnedOdds_AreCleanedUp()
    {
        var s = new PulseSettings { PinnedOdds = ["pm:1", "pm:1", "ks:KXBTCMAXY-26", "junk", "", .. Enumerable.Range(0, 40).Select(i => $"pm:{i + 10}")] };
        s.Normalize();
        Assert.Equal(PulseSettings.MaxPinnedOdds, s.PinnedOdds.Count);
        Assert.Equal(["pm:1", "ks:KXBTCMAXY-26"], s.PinnedOdds.Take(2));
        Assert.DoesNotContain("junk", s.PinnedOdds);
    }
}
