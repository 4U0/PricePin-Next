using System.Text.Json;
using PricePin.Core.Alerts;
using PricePin.Core.Events;
using PricePin.Core.History;
using PricePin.Core.Listings;
using PricePin.Core.Pulse;
using PricePin.Core.Settings;

namespace PricePin.Core.Tests;

public class EventsAndSmartAlertsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ---- Calendar: weekly feed ----

    private const string Week = """
        [
          {"title":"Core Retail Sales m/m","country":"USD","date":"2026-11-17T08:30:00-05:00","impact":"High","forecast":"0.4%","previous":"0.9%"},
          {"title":"FOMC Meeting Minutes","country":"USD","date":"2026-11-18T14:00:00-05:00","impact":"High","forecast":"","previous":""},
          {"title":"German ZEW","country":"EUR","date":"2026-11-17T05:00:00-05:00","impact":"Medium","forecast":"","previous":""},
          {"title":"Bank Holiday","country":"JPY","date":"2026-11-16T00:00:00-05:00","impact":"Holiday"},
          {"title":"ECB President Speaks","country":"EUR","date":"2026-11-19T09:00:00-05:00","impact":"High"},
          {"title":"","country":"USD","date":"2026-11-19T09:00:00-05:00","impact":"High"},
          {"title":"Broken date","country":"USD","date":"soon","impact":"High"}
        ]
        """;

    [Fact]
    public void ParseWeek_KeepsHighImpactOnly_InUtc()
    {
        var events = EventCalendarService.ParseWeek(Parse(Week))!;
        Assert.Equal(["Core Retail Sales m/m", "FOMC Meeting Minutes", "ECB President Speaks"], events.Select(e => e.Title));
        var sales = events[0];
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 13, 30, 0, TimeSpan.Zero), sales.Time);
        Assert.Equal(TimeSpan.Zero, sales.Time.Offset);
        Assert.Equal("USD", sales.Country);
        Assert.Equal("0.4%", sales.Forecast);
        Assert.Equal("0.9%", sales.Previous);
        Assert.Null(events[1].Forecast); // empty strings are no forecast
        Assert.All(events, e => Assert.Equal(EventKind.Macro, e.Kind));
        Assert.Equal(events.Count, events.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void ParseWeek_RejectsNonArrays() => Assert.Null(EventCalendarService.ParseWeek(Parse("""{"error":"rate limited"}""")));

    // ---- Calendar: known dates file ----

    [Fact]
    public void ParseCurated_ReadsEventsAndOnlyKeepsHttpsLinks()
    {
        var events = EventCalendarService.ParseCurated(Parse("""
            {"version":1,"events":[
              {"title":"FOMC rate decision","time":"2026-12-09T19:00:00Z","kind":"macro","country":"usd","url":"https://www.federalreserve.gov/"},
              {"title":"Big unlock","time":"2026-12-20T00:00:00Z","kind":"crypto","coins":["arb", 5, ""],"url":"javascript:alert(1)"},
              {"title":"No time"}
            ]}
            """))!;
        Assert.Equal(2, events.Count);
        Assert.Equal("USD", events[0].Country);
        Assert.Equal("https://www.federalreserve.gov/", events[0].Url);
        Assert.Equal(EventKind.Crypto, events[1].Kind);
        Assert.Equal(["ARB"], events[1].Coins);
        Assert.Null(events[1].Url);
    }

    [Fact]
    public void BundledEvents_AreBuiltIn_Sorted_AndInTheFuture()
    {
        var events = EventCalendarService.LoadBundled();
        Assert.True(events.Count >= 10, "data/events.json should be embedded in Core");
        Assert.Contains(events, e => e.Title == "FOMC rate decision");
        Assert.Contains(events, e => e.Title == "US CPI (inflation)");
        Assert.All(events, e => Assert.Equal(TimeSpan.Zero, e.Time.Offset));
        Assert.All(events, e => Assert.StartsWith("https://", e.Url));
    }

    [Fact]
    public void Merge_DropsKnownDatesTheWeeklyFeedAlreadyHas()
    {
        var fomcTime = new DateTimeOffset(2026, 12, 9, 19, 0, 0, TimeSpan.Zero);
        MarketEvent Ev(string title, DateTimeOffset t, string source, string country = "USD") =>
            new($"{source}:{title}", title, t, EventKind.Macro, country, null, null, null, [], source);

        var week = new[] { Ev("Federal Funds Rate", fomcTime, "ff"), Ev("CPI m/m", fomcTime.AddDays(1).AddHours(-5.5), "ff") };
        var curated = new[]
        {
            Ev("FOMC rate decision", fomcTime, "pp"),
            Ev("US CPI (inflation)", fomcTime.AddDays(1).AddHours(-5.5), "pp"),
            Ev("FOMC rate decision", fomcTime.AddDays(49), "pp"),
            Ev("FOMC rate decision", fomcTime, "pp", country: "EUR"), // another economy: kept
        };
        var merged = EventCalendarService.Merge(week, curated);
        Assert.Equal(4, merged.Count);
        Assert.Equal(["Federal Funds Rate", "FOMC rate decision", "CPI m/m", "FOMC rate decision"], merged.Select(e => e.Title));
        Assert.True(merged.Zip(merged.Skip(1)).All(p => p.First.Time <= p.Second.Time));
    }

    [Fact]
    public void Halving_IsTheNextMultipleOf210000()
    {
        var h = EventCalendarService.EstimateHalving(1_000_000, TimeSpan.FromMinutes(10), Now);
        Assert.Equal(1_050_000, h.HalvingHeight);
        Assert.Equal(50_000, h.BlocksLeft);
        Assert.Equal(Now + TimeSpan.FromMinutes(500_000), h.Estimated);

        var exactly = EventCalendarService.EstimateHalving(1_050_000, TimeSpan.FromMinutes(10), Now);
        Assert.Equal(1_260_000, exactly.HalvingHeight);

        var e = EventCalendarService.HalvingEvent(h);
        Assert.Equal(EventKind.Halving, e.Kind);
        Assert.True(e.Estimated);
        Assert.Equal("halving:1050000", e.Id);
    }

    [Fact]
    public void Reminders_FireOncePerEvent_InsideTheLeadTime()
    {
        MarketEvent At(string id, TimeSpan from) => new(id, id, Now + from, EventKind.Macro, "USD", null, null, null, [], "t");
        var events = new[] { At("past", TimeSpan.FromMinutes(-5)), At("soon", TimeSpan.FromMinutes(20)), At("later", TimeSpan.FromMinutes(45)) };
        var sent = new HashSet<string>();

        Assert.Equal(["soon"], EventReminders.Due(events, Now, TimeSpan.FromMinutes(30), sent).Select(e => e.Id));
        Assert.Empty(EventReminders.Due(events, Now + TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30), sent));
        Assert.Equal(["later"], EventReminders.Due(events, Now + TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30), sent).Select(e => e.Id));
        Assert.Empty(EventReminders.Due(events, Now, TimeSpan.Zero, new HashSet<string>()));
    }

    // ---- Fast moves ----

    [Fact]
    public void FastMove_FiresOnARiseWithinTheWindow_ThenStaysQuiet()
    {
        var d = new FastMoveDetector();
        var window = TimeSpan.FromMinutes(15);
        Assert.Null(d.Add("SOL", 100m, Now, 3m, window));
        Assert.Null(d.Add("SOL", 101m, Now.AddMinutes(5), 3m, window));
        var move = d.Add("SOL", 103.5m, Now.AddMinutes(10), 3m, window);
        Assert.NotNull(move);
        Assert.Equal(3.5m, move.ChangePercent);
        Assert.Equal(100m, move.From);

        // Keeps rising, but one move is one notification.
        Assert.Null(d.Add("SOL", 108m, Now.AddMinutes(12), 3m, window));
        Assert.Null(d.Add("SOL", 108.5m, Now.AddMinutes(20), 3m, window));
        // After the quiet time it can fire again, measured from prices seen since the last one.
        Assert.Null(d.Add("SOL", 108.5m, Now.AddMinutes(26), 3m, window));
        Assert.NotNull(d.Add("SOL", 112.5m, Now.AddMinutes(30), 3m, window));
    }

    [Fact]
    public void FastMove_FiresOnAFall_AndIgnoresMovesSpreadOverLongerThanTheWindow()
    {
        var d = new FastMoveDetector();
        var window = TimeSpan.FromMinutes(5);
        Assert.Null(d.Add("ETH", 100m, Now, 2m, window));
        Assert.Null(d.Add("ETH", 99m, Now.AddMinutes(4), 2m, window));
        // 100 → 97.9 took 8 minutes, longer than the window; only 99 → 97.9 (1.1%) counts.
        Assert.Null(d.Add("ETH", 97.9m, Now.AddMinutes(8), 2m, window));
        var move = d.Add("ETH", 96.5m, Now.AddMinutes(9), 2m, window);
        Assert.NotNull(move);
        Assert.True(move.ChangePercent < 0);
        Assert.Equal(99m, move.From);
    }

    [Fact]
    public void FastMove_KeepsCoinsApart_AndResets()
    {
        var d = new FastMoveDetector();
        var window = TimeSpan.FromMinutes(15);
        d.Add("BTC", 100m, Now, 5m, window);
        Assert.Null(d.Add("ETH", 106m, Now.AddMinutes(1), 5m, window));
        d.Reset();
        Assert.Null(d.Add("BTC", 110m, Now.AddMinutes(2), 5m, window));
        Assert.Null(d.Add("BTC", 0m, Now.AddMinutes(3), 5m, window));
    }

    // ---- Volume spikes ----

    private static List<Candle> Candles(int count, decimal volume, decimal lastHourVolume, DateTimeOffset end)
    {
        var list = new List<Candle>();
        for (var i = 0; i < count; i++)
        {
            var t = end - TimeSpan.FromMinutes(15 * (count - 1 - i));
            var inLastHour = i >= count - 4;
            list.Add(new Candle(t, 100m + i * 0.01m, 101m, 99m, 100m + (i + 1) * 0.01m, inLastHour ? lastHourVolume : volume));
        }
        return list;
    }

    [Fact]
    public void VolumeSpike_ComparesTheLastHourWithAnAverageHour()
    {
        // 92 earlier candles of 10 = 40 an hour; the last hour has 4 × 60 = 240, which is 6× normal.
        var candles = Candles(96, 10m, 60m, Now.AddMinutes(-5));
        var spike = VolumeSpikes.Check("SOL", candles, 5m, Now);
        Assert.NotNull(spike);
        Assert.Equal(6m, spike.Ratio);
        Assert.Null(VolumeSpikes.Check("SOL", candles, 10m, Now));
    }

    [Fact]
    public void VolumeSpike_IgnoresQuietMarkets_StaleData_AndShortHistory()
    {
        Assert.Null(VolumeSpikes.Check("SOL", Candles(96, 10m, 12m, Now.AddMinutes(-5)), 3m, Now));
        Assert.Null(VolumeSpikes.Check("SOL", Candles(96, 10m, 60m, Now.AddHours(-2)), 3m, Now));
        Assert.Null(VolumeSpikes.Check("SOL", Candles(20, 10m, 60m, Now.AddMinutes(-5)), 3m, Now));
        Assert.Null(VolumeSpikes.Check("SOL", Candles(96, 0m, 60m, Now.AddMinutes(-5)), 3m, Now));
    }

    // ---- Listings ----

    [Fact]
    public void Listings_FirstLookOnlyRemembers_ThenReportsNewCoins()
    {
        var known = new Dictionary<string, HashSet<string>>();
        Assert.Empty(ListingService.Diff(known, "Coinbase", ["BTC", "ETH"]));
        var found = ListingService.Diff(known, "Coinbase", ["BTC", "ETH", "NEW", "new"]);
        Assert.Equal([new NewListing("Coinbase", "NEW")], found);
        // Delisted and back again isn't new.
        Assert.Empty(ListingService.Diff(known, "Coinbase", ["BTC"]));
        Assert.Empty(ListingService.Diff(known, "Coinbase", ["BTC", "ETH", "NEW"]));
        // Another exchange starts fresh.
        Assert.Empty(ListingService.Diff(known, "Kraken", ["NEW"]));
    }

    [Fact]
    public void Listings_IgnoreAFloodOfNewCoins()
    {
        var known = new Dictionary<string, HashSet<string>>();
        ListingService.Diff(known, "Bybit", ["BTC"]);
        var flood = Enumerable.Range(0, ListingService.MaxNewAtOnce + 1).Select(i => $"C{i}").Append("BTC").ToList();
        Assert.Empty(ListingService.Diff(known, "Bybit", flood));
        Assert.Contains("C3", known["Bybit"]); // remembered, so they won't be announced later either
    }

    [Fact]
    public void Listings_ParseEachExchange_OnlyTradingMarkets()
    {
        var binance = ListingService.ParseBinance(Parse("""
            {"symbols":[{"baseAsset":"BTC","status":"TRADING"},{"baseAsset":"OLD","status":"BREAK"},{"baseAsset":"eth"}]}
            """))!;
        Assert.Equal(new[] { "BTC", "ETH" }.ToHashSet(), binance.ToHashSet());

        var coinbase = ListingService.ParseCoinbase(Parse("""
            [{"base_currency":"SOL","status":"online"},{"base_currency":"OFF","status":"delisted"},{"base_currency":"HALT","status":"online","trading_disabled":true}]
            """))!;
        Assert.Equal(["SOL"], coinbase);

        var kraken = ListingService.ParseKraken(Parse("""
            {"result":{"XXBTZUSD":{"wsname":"XBT/USD","status":"online"},"XDGUSD":{"wsname":"XDG/USD"},"X":{"wsname":"BAD/USD","status":"cancel_only"}}}
            """))!;
        Assert.Equal(new[] { "BTC", "DOGE" }.ToHashSet(), kraken.ToHashSet());

        var bybit = ListingService.ParseBybit(Parse("""
            {"retCode":0,"result":{"category":"spot","list":[{"baseCoin":"PEPE","status":"Trading"},{"baseCoin":"PRE","status":"PreLaunch"}]}}
            """))!;
        Assert.Equal(["PEPE"], bybit);

        Assert.Null(ListingService.ParseBybit(Parse("""{"retCode":10001}""")));
        Assert.Null(ListingService.ParseCoinbase(Parse("""{"message":"error"}""")));
    }

    // ---- Settings ----

    [Fact]
    public void Settings_SnapToOfferedChoices()
    {
        var s = new AppSettings
        {
            SmartAlerts = new SmartAlertSettings { FastMovePercent = 4m, FastMoveMinutes = 50, VolumeSpikeFactor = 100m },
            Pulse = new PulseSettings { CalendarRemindMinutes = 7 },
        };
        s.Normalize();
        Assert.Contains(s.SmartAlerts.FastMovePercent, SmartAlertSettings.MovePercents);
        Assert.Equal(60, s.SmartAlerts.FastMoveMinutes);
        Assert.Equal(10m, s.SmartAlerts.VolumeSpikeFactor);
        Assert.Equal(0, s.Pulse.CalendarRemindMinutes);

        var json = JsonSerializer.Serialize(new AppSettings());
        var back = JsonSerializer.Deserialize<AppSettings>(json.Replace("\"SmartAlerts\":", "\"Ignored\":"))!;
        back.Normalize();
        Assert.NotNull(back.SmartAlerts); // old files without the section still load
    }
}
