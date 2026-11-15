using System.Text;
using System.Text.Json;
using PricePin.Core.Fees;
using PricePin.Core.News;
using PricePin.Core.Pulse;
using PricePin.Core.Radar;
using PricePin.Core.Settings;

namespace PricePin.Core.Tests;

public class PulseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly NewsCoin[] Coins =
    [
        new("BTC", "BTC", "Bitcoin"),
        new("ETH", "ETH", "Ethereum"),
        new("SOL", "SOL", "Solana"),
        new("OP", "OP", "Optimism"),
        new("ONE", "ONE", "Harmony"),
        new("XLM", "XLM", "Stellar"),
        new("dex:solana:abc", "WIF", "dogwifhat"),
    ];

    // ---- News ----

    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE rss [<!ENTITY boom "boom">]>
        <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel>
            <title>Example</title>
            <item>
              <title><![CDATA[Bitcoin tops $100K as <b>ETF</b> inflows &amp; SOL rally]]></title>
              <link>https://example.com/a</link>
              <pubDate>Sun, 15 Nov 2026 11:00:00 GMT</pubDate>
            </item>
            <item>
              <title>Not safe link</title>
              <link>javascript:alert(1)</link>
              <pubDate>Sun, 15 Nov 2026 11:00:00 GMT</pubDate>
            </item>
            <item>
              <title>Old news</title>
              <link>https://example.com/old</link>
              <pubDate>Mon, 09 Nov 2026 11:00:00 EST</pubDate>
            </item>
            <item>
              <title>The one new solar project</title>
              <link>https://example.com/b</link>
              <dc:date>2026-11-15T10:30:00Z</dc:date>
            </item>
          </channel>
        </rss>
        """;

    private const string Atom = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <title type="html">Ethereum devs set date for upgrade</title>
            <link rel="replies" href="https://example.org/comments"/>
            <link rel="alternate" href="https://example.org/eth"/>
            <published>2026-11-15T09:00:00+01:00</published>
          </entry>
          <entry>
            <title>BITCOIN TOPS $100K AS ETF INFLOWS &amp; SOL RALLY</title>
            <link href="https://example.org/dup"/>
            <updated>2026-11-15T11:30:00Z</updated>
          </entry>
        </feed>
        """;

    [Fact]
    public void Reads_rss_and_atom_and_skips_unsafe_links()
    {
        var rss = NewsService.ParseFeed(Rss, "Example");
        Assert.Equal(3, rss.Count);
        Assert.Equal("Bitcoin tops $100K as ETF inflows & SOL rally", rss[0].Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 11, 0, 0, TimeSpan.Zero), rss[0].Published);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 16, 0, 0, TimeSpan.Zero), rss[1].Published.ToUniversalTime());
        Assert.Equal("https://example.com/b", rss[2].Url);

        var atom = NewsService.ParseFeed(Atom, "Other");
        Assert.Equal("https://example.org/eth", atom[0].Url);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.Zero), atom[0].Published.ToUniversalTime());
    }

    [Fact]
    public void Merges_feeds_newest_first_without_old_or_duplicate_headlines()
    {
        var items = NewsService.ParseFeed(Rss, "Example").Concat(NewsService.ParseFeed(Atom, "Other"));
        var merged = NewsService.Merge(items, Coins, Now);

        Assert.Equal(["https://example.com/a", "https://example.com/b", "https://example.org/eth"], merged.Select(i => i.Url));
        Assert.Equal(["BTC", "SOL"], merged[0].Coins);
        Assert.Empty(merged[1].Coins); // "one" and "solar" are not coins
        Assert.Equal(["ETH"], merged[2].Coins);
    }

    [Theory]
    [InlineData("Solana and BITCOIN climb", new[] { "BTC", "SOL" })]
    [InlineData("SOL rallies while ETH stalls", new[] { "ETH", "SOL" })]
    [InlineData("Sol is a word in Spanish", new string[0])]
    [InlineData("OP and ONE are too short or too common", new string[0])]
    [InlineData("Optimism airdrop and dogwifhat meme", new[] { "OP", "dex:solana:abc" })]
    [InlineData("WIF jumps 20%", new[] { "dex:solana:abc" })]
    [InlineData("Bitcoiners rejoice", new string[0])]
    [InlineData("A stellar week for bitcoin", new[] { "BTC" })]
    [InlineData("Stellar partners with bank", new[] { "XLM" })]
    public void Tags_headlines_with_the_coins_they_mention(string title, string[] expected) =>
        Assert.Equal(expected.Order(), NewsService.Tag(title, Coins).Order());

    [Fact]
    public void Broken_xml_is_reported_not_swallowed()
    {
        Assert.ThrowsAny<System.Xml.XmlException>(() => NewsService.ParseFeed("<rss><channel><item>", "x"));
        Assert.Empty(NewsService.ParseFeed("<rss><channel/></rss>", "x"));
    }

    // ---- Radar ----

    private static ReadOnlySpan<byte> U(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Reads_binance_trades()
    {
        var e = RadarService.ParseBinanceAggTrade(U("""{"stream":"btcusdt@aggTrade","data":{"e":"aggTrade","s":"BTCUSDT","p":"97000.5","q":"10","T":1763208000000,"m":true}}"""))!;
        Assert.Equal((RadarKind.WhaleSell, "BTC", "Binance"), (e.Kind, e.Symbol, e.Exchange));
        Assert.Equal(970_005m, e.ValueUsd);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1763208000000), e.Time);
        Assert.True(e.IsWhale);

        var buy = RadarService.ParseBinanceAggTrade(U("""{"e":"aggTrade","s":"ETHUSDT","p":"3000","q":"1","T":1,"m":false}"""))!;
        Assert.Equal(RadarKind.WhaleBuy, buy.Kind);
        Assert.Null(RadarService.ParseBinanceAggTrade(U("""{"result":null,"id":1}""")));
    }

    [Fact]
    public void Reads_coinbase_matches_from_the_takers_side()
    {
        var e = RadarService.ParseCoinbaseMatch(U("""{"type":"match","product_id":"ETH-USD","size":"200","price":"3000","side":"sell","time":"2026-11-15T01:00:00.123Z"}"""))!;
        Assert.Equal((RadarKind.WhaleBuy, "ETH", "Coinbase", 600_000m), (e.Kind, e.Symbol, e.Exchange, e.ValueUsd));
        Assert.Null(RadarService.ParseCoinbaseMatch(U("""{"type":"match","product_id":"ETH-EUR","size":"1","price":"1","side":"buy"}""")));
        Assert.Null(RadarService.ParseCoinbaseMatch(U("""{"type":"subscriptions","channels":[]}""")));
        Assert.Contains("\"BTC-USD\"", RadarService.CoinbaseSubscribe(["BTC"]));
    }

    [Fact]
    public void Reads_liquidations_from_binance_and_bybit()
    {
        var b = RadarService.ParseBinanceForceOrder(U("""{"e":"forceOrder","E":1,"o":{"s":"1000PEPEUSDT","S":"SELL","q":"100000000","p":"0.01","ap":"0.012","z":"100000000","T":1763208000000}}"""))!;
        Assert.Equal((RadarKind.LongLiquidated, "PEPE"), (b.Kind, b.Symbol));
        Assert.Equal(1_200_000m, b.ValueUsd);

        var noFill = RadarService.ParseBinanceForceOrder(U("""{"e":"forceOrder","o":{"s":"BTCUSDT","S":"BUY","q":"2","p":"95000","ap":"0","z":"0","T":1}}"""))!;
        Assert.Equal((RadarKind.ShortLiquidated, 190_000m), (noFill.Kind, noFill.ValueUsd));

        var y = RadarService.ParseBybitLiquidations(U("""{"topic":"allLiquidation.SOLUSDT","type":"snapshot","ts":1,"data":[{"T":1763208000000,"s":"SOLUSDT","S":"Buy","v":"1000","p":"200"},{"T":1,"s":"SOLUSDT","S":"Sell","v":"1","p":"200"}]}""")).ToList();
        Assert.Equal(2, y.Count);
        Assert.Equal((RadarKind.LongLiquidated, "SOL", "Bybit", 200_000m), (y[0].Kind, y[0].Symbol, y[0].Exchange, y[0].ValueUsd));
        Assert.Equal(RadarKind.ShortLiquidated, y[1].Kind);
        Assert.Empty(RadarService.ParseBybitLiquidations(U("""{"success":true,"op":"subscribe"}""")));
        Assert.Contains("allLiquidation.BTCUSDT", RadarService.BybitSubscribe(["BTC"]));
    }

    [Fact]
    public void Builds_stream_urls_and_formats_sizes()
    {
        Assert.Equal("wss://stream.binance.com:9443/stream?streams=btcusdt@aggTrade/ethusdt@aggTrade",
            RadarService.BinanceTradesUri(["BTC", "ETH"]).ToString());
        Assert.Equal("$1.25M", RadarService.FormatUsd(1_250_000m));
        Assert.Equal("$500K", RadarService.FormatUsd(500_000m));
    }

    // ---- Fees ----

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Reads_bitcoin_fees()
    {
        var f = FeeService.ParseMempool(J("""{"fastestFee":12,"halfHourFee":10,"hourFee":8,"economyFee":4,"minimumFee":2}"""))!;
        Assert.Equal((12m, 10m, 8m, 4m), (f.Fastest, f.HalfHour, f.Hour, f.Economy));
        Assert.Null(FeeService.ParseMempool(J("""{"error":"x"}""")));
        Assert.Equal(12m * 140 / 100_000_000m, FeeService.BitcoinTxCostBtc(12));
    }

    [Fact]
    public void Reads_ethereum_gas_from_a_json_rpc_batch()
    {
        // 0x3b9aca00 = 1 gwei; 0x5f5e100 = 0.1 gwei. Answers can come back in any order.
        var gas = FeeService.ParseRpcBatch(J("""
            [{"jsonrpc":"2.0","id":2,"result":"0x5f5e100"},
             {"jsonrpc":"2.0","id":1,"result":{"number":"0x1","baseFeePerGas":"0x2540be400"}}]
            """))!;
        Assert.Equal(10m, gas.BaseFeeGwei);
        Assert.Equal(0.1m, gas.PriorityFeeGwei);
        Assert.Equal(10.1m * 21_000m / 1_000_000_000m, gas.TransferCostEth);

        var noTip = FeeService.ParseRpcBatch(J("""[{"id":1,"result":{"baseFeePerGas":"0x3b9aca00"}},{"id":2,"error":{"code":-32601}}]"""))!;
        Assert.Equal((1m, 0.1m), (noTip.BaseFeeGwei, noTip.PriorityFeeGwei));
        Assert.Null(FeeService.ParseRpcBatch(J("""{"id":1,"result":null}""")));
        Assert.Null(FeeService.ParseHex("12"));
        Assert.Equal(255, (int)FeeService.ParseHex("0xff")!.Value);
    }

    [Fact]
    public void Fee_alarm_fires_once_and_rearms_after_a_rise()
    {
        var alarm = new FeeAlarm();
        Assert.False(alarm.Check(10, 15));
        Assert.True(alarm.Check(10, 9));
        Assert.False(alarm.Check(10, 8));
        Assert.False(alarm.Check(10, 11)); // still within 20%
        Assert.False(alarm.Check(10, 9.5m));
        Assert.False(alarm.Check(10, 12)); // rearms
        Assert.True(alarm.Check(10, 10));
        Assert.False(alarm.Check(null, 1));
    }

    [Fact]
    public void Pulse_settings_have_safe_defaults()
    {
        var s = new AppSettings { Pulse = null! };
        s.Normalize();
        Assert.Equal(500_000m, s.Pulse.MinWhaleUsd);
        Assert.True(s.Pulse.RadarOnlyMyCoins);

        s.Pulse.MinLiquidationUsd = -5;
        s.Pulse.EthGasBelowGwei = 0;
        s.Pulse.MinWhaleUsd = 1;
        s.Normalize();
        Assert.Equal(100_000m, s.Pulse.MinLiquidationUsd);
        Assert.Null(s.Pulse.EthGasBelowGwei);
        Assert.Equal(10_000m, s.Pulse.MinWhaleUsd);
        Assert.False(s.Widgets[0].ShowNews);
    }
}
