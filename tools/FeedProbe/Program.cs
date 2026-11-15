// Usage: dotnet run --project tools/FeedProbe [seconds] [coins...]
// Example: dotnet run --project tools/FeedProbe 10 BTC ETH SUI
using System.Diagnostics;
using PricePin.Core.Dex;
using PricePin.Core.Feeds;
using PricePin.Core.History;
using PricePin.Core.Icons;
using PricePin.Core.Market;
using PricePin.Core.Fees;
using PricePin.Core.News;
using PricePin.Core.Radar;

var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 12;
var coins = args.Skip(1).DefaultIfEmpty("BTC").Concat(args.Length > 1 ? [] : ["ETH", "SOL"]).ToArray();
var symbols = coins.Select(c => new MarketSymbol(c, "USD")).ToList();

Console.WriteLine($"Probing feeds for {seconds}s with {string.Join(", ", coins)}\n");

IMarketFeed[] feeds = [new BinanceFeed(), new CoinbaseFeed(), new KrakenFeed(), new BinanceRestFeed(), new CoinbaseRestFeed()];
var streamingOk = 0;

foreach (var feed in feeds)
{
    var ticks = 0;
    var bySymbol = new Dictionary<string, Ticker>();
    var firstTickMs = -1L;
    var sw = Stopwatch.StartNew();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    string result;
    try
    {
        await feed.RunSessionAsync(symbols, t =>
        {
            lock (bySymbol)
            {
                ticks++;
                if (firstTickMs < 0) firstTickMs = sw.ElapsedMilliseconds;
                bySymbol[t.Base] = bySymbol.TryGetValue(t.Base, out var prev) && t.ChangePercent24h is null
                    ? t with { ChangePercent24h = prev.ChangePercent24h }
                    : t;
            }
        }, () => { }, cts.Token);
        result = ticks > 0 ? "OK" : "NO DATA";
    }
    catch (FeedException ex)
    {
        result = $"{ex.Failure.ToString().ToUpperInvariant()}: {ex.Message}";
    }

    if (ticks > 0 && feed.IsStreaming)
        streamingOk++;

    Console.WriteLine($"{feed.Name,-18} {result}");
    Console.WriteLine($"{"",-18} {ticks} updates, first after {(firstTickMs < 0 ? "-" : firstTickMs + " ms")}");
    foreach (var (coin, t) in bySymbol.OrderBy(kv => kv.Key))
        Console.WriteLine($"{"",-18} {coin,-5} {t.Last,14}  24h {t.ChangePercent24h?.ToString("0.00") ?? "?",6}%");
    Console.WriteLine();
}

Console.WriteLine("History (24h sparkline):");
var history = new PriceHistoryService();
foreach (var sym in symbols)
{
    var closes = await history.GetLast24hAsync(sym);
    Console.WriteLine($"  {sym,-9} {closes.Count} points" + (closes.Count > 0 ? $", {closes[0]} → {closes[^1]}" : ""));
}

Console.WriteLine("\nDetail chart candles:");
var candles = new CandleService();
foreach (var range in Enum.GetValues<ChartRange>())
{
    var series = await candles.GetAsync(symbols[0], range);
    Console.WriteLine($"  {symbols[0],-9} {range,-6} " + (series is null
        ? "none"
        : $"{series.Candles.Count} candles from {series.Source}, {series.Candles[0].Time:yyyy-MM-dd HH:mm} → {series.Candles[^1].Time:yyyy-MM-dd HH:mm}"));
}

Console.WriteLine("\nMarket overview:");
var overview = await new MarketOverviewService().GetAsync("USD");
Console.WriteLine($"  Market cap {overview.TotalMarketCap?.ToString("N0") ?? "?"} {overview.MarketCapCurrency}, 24h {overview.MarketCapChange24h?.ToString("0.00") ?? "?"}%");
Console.WriteLine($"  BTC dominance {overview.BtcDominance?.ToString("0.0") ?? "?"}%, ETH {overview.EthDominance?.ToString("0.0") ?? "?"}%");
Console.WriteLine($"  Fear & Greed {overview.FearGreed?.ToString() ?? "?"} ({overview.FearGreedLevel?.ToString() ?? "?"})");

Console.WriteLine("\nLogos:");
var iconDir = Path.Combine(Path.GetTempPath(), "pricepin-probe-icons");
var icons = new CoinIconService(iconDir);
foreach (var coin in coins.Append("SUI").Distinct())
{
    var path = await icons.GetIconPathAsync(coin);
    Console.WriteLine($"  {coin,-5} {(path is null ? "not found" : new FileInfo(path).Length + " bytes")}");
}

Console.WriteLine("\nSparkline history by period:");
foreach (var period in Enum.GetValues<ChangePeriod>())
{
    var closes = await history.GetAsync(symbols[0], period);
    Console.WriteLine($"  {symbols[0],-9} {period,-5} {closes.Count} points" + (closes.Count > 0 ? $", {closes[0]} → {closes[^1]}" : ""));
}

Console.WriteLine("\nFiat rates (USD →):");
foreach (var q in new[] { "EUR", "TRY", "GBP" })
    Console.WriteLine($"  {q} {(await FiatRates.Shared.UsdToAsync(q))?.ToString("0.####") ?? "unavailable"}");

Console.WriteLine("\nDEX tokens (DexScreener + GeckoTerminal):");
var dexOk = false;
try
{
    foreach (var query in new[] { "bonk", "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263", "0x6982508145454Ce325dDbE47a25d4ec3d2311933" })
    {
        var found = await DexScreenerClient.SearchAsync(query);
        Console.WriteLine($"  search \"{query}\": {found.Count} tokens");
        foreach (var p in found.Take(3))
            Console.WriteLine($"    {p.BaseSymbol,-8} {p.Chain,-9} {p.DexId,-12} ${p.PriceUsd} 1h {p.Change1h?.ToString("0.00") ?? "?"}% 24h {p.Change24h?.ToString("0.00") ?? "?"}% liq ${p.LiquidityUsd:N0} logo {(p.ImageUrl is null ? "no" : "yes")}");
    }

    var bonk = (await DexScreenerClient.SearchAsync("DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263")).FirstOrDefault();
    if (bonk is not null)
    {
        var token = bonk.ToToken();
        PricePin.Core.Coins.CoinCatalog.Register(token);
        var dex = new DexPriceService();
        Ticker? tick = null;
        dex.TickReceived += t => tick = t;
        await dex.PollOnceAsync([token], "EUR", CancellationToken.None);
        Console.WriteLine($"  poll {token.Symbol} in EUR: {(tick is null ? "no price" : $"{tick.Last} (24h {tick.ChangePercent24h}%, mcap {tick.MarketCap:N0})")}");
        dexOk = tick is not null;

        foreach (var period in Enum.GetValues<ChangePeriod>())
        {
            var closes = await history.GetAsync(new MarketSymbol(token.Key, "USD"), period);
            Console.WriteLine($"  sparkline {period,-5} {closes.Count} points");
        }
        foreach (var range in Enum.GetValues<ChartRange>())
        {
            var started = Stopwatch.StartNew();
            var series = await candles.GetAsync(new MarketSymbol(token.Key, "USD"), range);
            Console.WriteLine($"  chart {range,-6} " + (series is null ? "none" : $"{series.Candles.Count} candles from {series.Source}") + $" ({started.ElapsedMilliseconds} ms)");
            if (series is null && token.PairAddress is { } pool)
                Console.WriteLine("    " + await GeckoTerminalClient.DescribeAsync(token.Chain, pool, range == ChartRange.Year ? "day" : "hour", range == ChartRange.Year ? 1 : 4, range == ChartRange.Year ? 365 : 180));
        }
        var logo = await icons.GetTokenIconPathAsync(token.Key, token.ImageUrl);
        Console.WriteLine($"  logo {(logo is null ? "not found" : new FileInfo(logo).Length + " bytes")}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
}
Console.WriteLine($"  DEX prices {(dexOk ? "OK" : "NOT WORKING")}");

Console.WriteLine("\nNews feeds:");
var newsOk = 0;
foreach (var (name, urls) in NewsService.Feeds)
{
    foreach (var url in urls)
    {
        var (items, error) = await NewsService.FetchFeedAsync(name, url, CancellationToken.None);
        var dated = items?.Count(i => i.Published != DateTimeOffset.MinValue) ?? 0;
        Console.WriteLine($"  {name,-17} {(items is null ? $"FAILED ({error})" : $"{items.Count} items, {dated} dated")}  {url}" +
                          (items is { Count: > 0 } ? $"\n{"",-19}newest \"{items.OrderByDescending(i => i.Published).First().Title}\"" : ""));
        if (items is not null)
        {
            newsOk++;
            break;
        }
    }
}
var newsCoins = PricePin.Core.Coins.CoinCatalog.All.Select(c => new NewsCoin(c.Symbol, c.Symbol, c.Name)).ToList();
var (merged, okFeeds) = await new NewsService().FetchAsync(newsCoins);
Console.WriteLine($"  merged: {merged.Count} headlines from {okFeeds} feeds, {merged.Count(i => i.Coins.Count > 0)} tagged with coins");
foreach (var item in merged.Take(5))
    Console.WriteLine($"    [{string.Join(",", item.Coins)}] {item.Source}: {item.Title}");

Console.WriteLine("\nNetwork fees:");
var fees = new FeeService();
var btcFees = await fees.FetchBitcoinAsync();
Console.WriteLine($"  mempool.space   {(btcFees is null ? "FAILED" : $"fastest {btcFees.Fastest}, 30m {btcFees.HalfHour}, 1h {btcFees.Hour}, economy {btcFees.Economy} sat/vB")}");
foreach (var rpc in FeeService.EthereumRpcUrls)
{
    var (gas, gasError) = await FeeService.FetchEthereumAsync(rpc, CancellationToken.None);
    Console.WriteLine($"  {new Uri(rpc).Host,-28} {(gas is null ? $"FAILED ({gasError})" : $"base {gas.BaseFeeGwei} + tip {gas.PriorityFeeGwei} gwei")}");
}

Console.WriteLine("\nWhale and liquidation streams (every trade counted, no size filter):");
async Task ProbeStream(string name, Uri uri, IReadOnlyList<string> subscribe, Func<ReadOnlyMemory<byte>, int> parse, int secs)
{
    var events = 0;
    var messages = 0;
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(secs));
    string result;
    try
    {
        await WebSocketSession.RunAsync(name, uri, subscribe, span =>
        {
            messages++;
            try { events += parse(span.ToArray()); } catch (System.Text.Json.JsonException) { }
        }, TimeSpan.FromSeconds(secs + 5), TimeSpan.FromHours(1), cts.Token);
        result = "OK";
    }
    catch (OperationCanceledException)
    {
        result = "OK";
    }
    catch (FeedException ex)
    {
        result = $"{ex.Failure.ToString().ToUpperInvariant()}: {ex.Message}";
    }
    Console.WriteLine($"  {name,-22} {result}, {messages} messages, {events} events");
}
await ProbeStream("Binance trades", RadarService.BinanceTradesUri(["BTC", "ETH"]), [], m => RadarService.ParseBinanceAggTrade(m.Span) is null ? 0 : 1, 10);
await ProbeStream("Coinbase trades", new Uri("wss://ws-feed.exchange.coinbase.com"), [RadarService.CoinbaseSubscribe(["BTC", "ETH"])],
    m => RadarService.ParseCoinbaseMatch(m.Span) is null ? 0 : 1, 10);
await ProbeStream("Binance liquidations", new Uri("wss://fstream.binance.com/ws/!forceOrder@arr"), [], m => RadarService.ParseBinanceForceOrder(m.Span) is null ? 0 : 1, 30);
await ProbeStream("Bybit liquidations", new Uri("wss://stream.bybit.com/v5/public/linear"), [RadarService.BybitSubscribe(["BTC", "ETH", "SOL", "XRP", "DOGE"])],
    m => RadarService.ParseBybitLiquidations(m.Span).Count(), 30);

Console.WriteLine($"\n{streamingOk} of 3 streaming feeds delivered data.");
return streamingOk > 0 ? 0 : 1;
