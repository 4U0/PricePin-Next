// Usage: dotnet run --project tools/FeedProbe [seconds] [coins...]
// Example: dotnet run --project tools/FeedProbe 10 BTC ETH SUI
using System.Diagnostics;
using PricePin.Core.Feeds;
using PricePin.Core.History;
using PricePin.Core.Icons;
using PricePin.Core.Market;

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

Console.WriteLine("\nLogos:");
var iconDir = Path.Combine(Path.GetTempPath(), "pricepin-probe-icons");
var icons = new CoinIconService(iconDir);
foreach (var coin in coins.Append("SUI").Distinct())
{
    var path = await icons.GetIconPathAsync(coin);
    Console.WriteLine($"  {coin,-5} {(path is null ? "not found" : new FileInfo(path).Length + " bytes")}");
}

Console.WriteLine($"\n{streamingOk} of 3 streaming feeds delivered data.");
return streamingOk > 0 ? 0 : 1;
