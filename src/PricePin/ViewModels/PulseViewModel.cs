using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Coins;
using PricePin.Core.Fees;
using PricePin.Core.News;
using PricePin.Core.Pulse;
using PricePin.Core.Radar;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>One headline in the news list or a ticker bar.</summary>
public sealed partial class HeadlineViewModel : ObservableObject
{
    public HeadlineViewModel(NewsItem item)
    {
        Item = item;
        CoinsText = string.Join(" · ", item.Coins.Select(CoinCatalog.DisplaySymbol));
        RefreshAge(DateTimeOffset.UtcNow);
    }

    public NewsItem Item { get; }
    public string Title => Item.Title;
    public string Source => Item.Source;
    public string CoinsText { get; }
    public bool HasCoins => Item.Coins.Count > 0;
    public string CoinsSeparator => HasCoins ? "  ·  " : "";

    [ObservableProperty]
    private string _ageText = "";

    /// <summary>"CoinDesk · 12m ago".</summary>
    public string SourceLine => $"{Source} · {AgeText}";

    partial void OnAgeTextChanged(string value) => OnPropertyChanged(nameof(SourceLine));

    public void RefreshAge(DateTimeOffset now) => AgeText = PulseViewModel.FormatAgo(now - Item.Published);

    [RelayCommand]
    private void Open() => PulseViewModel.OpenLink(Item.Url);
}

/// <summary>One whale trade or liquidation in the radar list.</summary>
public sealed class RadarEventViewModel(RadarEvent e)
{
    public RadarEvent Event { get; } = e;

    /// <summary>Whale buys and liquidated shorts push the price up; shown in green.</summary>
    public bool IsUp => Event.Kind is RadarKind.WhaleBuy or RadarKind.ShortLiquidated;

    public bool IsWhale => Event.IsWhale;
    public string Symbol => Event.Symbol;
    public string ValueText => RadarText.Usd(Event.ValueUsd);
    public string KindText => Loc.T(Event.Kind switch
    {
        RadarKind.WhaleBuy => "Radar.WhaleBuy",
        RadarKind.WhaleSell => "Radar.WhaleSell",
        RadarKind.LongLiquidated => "Radar.LongLiquidated",
        _ => "Radar.ShortLiquidated",
    });

    /// <summary>Whale: 🐋; liquidation: 💥 (as Segoe glyphs that work on every Windows 10/11).</summary>
    public string Glyph => IsWhale ? "" : "";

    public string DetailText => Loc.F("Radar.Detail", Event.Exchange, CoinRowViewModel.FormatPrice(Event.Price, "USD"));
    public string TimeText => Event.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
}

internal static class RadarText
{
    public static string Usd(decimal v) => v >= 1_000_000_000m
        ? "$" + (v / 1_000_000_000m).ToString("0.##", CultureInfo.CurrentCulture) + "B"
        : v >= 1_000_000m
            ? "$" + (v / 1_000_000m).ToString("0.##", CultureInfo.CurrentCulture) + "M"
            : "$" + (v / 1_000m).ToString("0", CultureInfo.CurrentCulture) + "K";
}

/// <summary>The latest network fees, short enough for a widget.</summary>
public sealed partial class FeeSummaryViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasData;

    /// <summary>"12 sat/vB".</summary>
    [ObservableProperty] private string _bitcoinText = "—";

    /// <summary>"10.1 gwei".</summary>
    [ObservableProperty] private string _ethereumText = "—";

    [ObservableProperty] private string _toolTip = "";
}

/// <summary>
/// News, the whale and liquidation radar, and network fees. Each part only runs while
/// something uses it: the Pulse window, a widget that shows it, or a notification the user
/// turned on.
/// </summary>
public sealed partial class PulseViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan NewsEvery = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FeesEvery = TimeSpan.FromSeconds(60);
    private const int MaxRadarRows = 200;
    private const int MaxWidgetHeadlines = 8;

    private readonly AppViewModel _app;
    private readonly NewsService _news = new();
    private readonly FeeService _fees = new();
    private readonly RadarService _radar = new();
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _radarFlush;
    private readonly ConcurrentQueue<RadarEvent> _incoming = new();
    private readonly List<RadarEvent> _lastHour = [];
    private readonly HashSet<string> _seenUrls = new(StringComparer.Ordinal);
    private readonly FeeAlarm _gasAlarm = new();
    private readonly FeeAlarm _btcAlarm = new();
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<NewsItem> _allNews = [];
    private DateTime _newsAt = DateTime.MinValue;
    private DateTime _feesAt = DateTime.MinValue;
    private DateTime _lastRadarNotify = DateTime.MinValue;
    private bool _newsLoading;
    private bool _feesLoading;
    private bool _radarRunning;
    private string _radarConfig = "";
    private int _windowsOpen;
    private bool _watchingPrices;
    private string? _whaleSource;
    private string? _liquidationSource;

    public PulseViewModel(AppViewModel app)
    {
        _app = app;
        var s = Settings;
        _newsOnlyMyCoins = s.NewsOnlyMyCoins;
        _notifyHeldCoinNews = s.NotifyHeldCoinNews;
        _radarOnlyMyCoins = s.RadarOnlyMyCoins;
        _notifyRadar = s.NotifyRadar;
        _minWhale = Closest(PulseSettings.WhaleThresholds, s.MinWhaleUsd);
        _minLiquidation = Closest(PulseSettings.LiquidationThresholds, s.MinLiquidationUsd);
        _notifyRadarAt = Closest(PulseSettings.NotifyThresholds, s.NotifyRadarUsd);
        _gasBelowText = s.EthGasBelowGwei?.ToString(CultureInfo.CurrentCulture) ?? "";
        _btcBelowText = s.BtcFeeBelowSatVb?.ToString(CultureInfo.CurrentCulture) ?? "";

        WhaleOptions = Options(PulseSettings.WhaleThresholds, _minWhale, v => MinWhale = v);
        LiquidationOptions = Options(PulseSettings.LiquidationThresholds, _minLiquidation, v => MinLiquidation = v);
        NotifyOptions = Options(PulseSettings.NotifyThresholds, _notifyRadarAt, v => NotifyRadarAt = v);

        Calendar = new CalendarViewModel(app, () => IsWindowOpen);
        Calendar.NotificationRequested += (title, body) => NotificationRequested?.Invoke(title, body);
        Odds = new OddsViewModel(app, () => IsWindowOpen, TaggableCoins);
        Odds.NotificationRequested += (title, body) => NotificationRequested?.Invoke(title, body);

        _radar.EventReceived += e => _incoming.Enqueue(e);
        _radar.SourceChanged += OnRadarSourceChanged;

        var dispatcher = Dispatcher.CurrentDispatcher;
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => OnTick(), dispatcher);
        _radarFlush = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => FlushRadar(), dispatcher);
        _radarFlush.Stop();
        _tick.Stop();
    }

    private PulseSettings Settings => _app.Settings.Pulse;

    /// <summary>Raised for a notification: title and body.</summary>
    public event Action<string, string>? NotificationRequested;

    /// <summary>Raised when the headlines change, so ticker bars can rebuild their strip.</summary>
    public event EventHandler? HeadlinesChanged;

    public void Start()
    {
        _tick.Start();
        UpdateDemand();
    }

    // ---- Window tabs --------------------------------------------------------------------

    /// <summary>"News", "Radar", "Fees", "Calendar" or "Odds".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsTab), nameof(IsRadarTab), nameof(IsFeesTab), nameof(IsCalendarTab), nameof(IsOddsTab))]
    private string _tab = "News";

    public bool IsNewsTab => Tab == "News";
    public bool IsRadarTab => Tab == "Radar";
    public bool IsFeesTab => Tab == "Fees";
    public bool IsCalendarTab => Tab == "Calendar";
    public bool IsOddsTab => Tab == "Odds";

    /// <summary>Upcoming market events, the halving countdown and reminders.</summary>
    public CalendarViewModel Calendar { get; }

    /// <summary>Prediction market odds from Polymarket and Kalshi.</summary>
    public OddsViewModel Odds { get; }

    [RelayCommand]
    private Task Reload() => IsFeesTab ? LoadFeesAsync() : IsNewsTab ? LoadNewsAsync() : IsCalendarTab ? Calendar.Reload() : IsOddsTab ? Odds.Reload() : Task.CompletedTask;

    // ---- Who needs what -----------------------------------------------------------------

    public bool IsWindowOpen => _windowsOpen > 0;

    public void WindowOpened()
    {
        _windowsOpen++;
        UpdateDemand();
    }

    public void WindowClosed()
    {
        _windowsOpen = Math.Max(0, _windowsOpen - 1);
        UpdateDemand();
    }

    private bool NeedsNews => IsWindowOpen || NotifyHeldCoinNews || _app.Widgets.Any(w => w.ShowNews);
    private bool NeedsFees => IsWindowOpen || Settings.EthGasBelowGwei is not null || Settings.BtcFeeBelowSatVb is not null || _app.Widgets.Any(w => w.ShowFees);
    private bool NeedsRadar => IsWindowOpen || NotifyRadar;

    /// <summary>Starts or stops each part after a window, widget or setting changed.</summary>
    public void UpdateDemand()
    {
        if (_lifetime.IsCancellationRequested)
            return;
        OnTick();

        if (NeedsRadar)
            StartRadar();
        else
            StopRadar();

        // The window shows fee costs in the user's currency, which needs BTC and ETH prices.
        if (IsWindowOpen != _watchingPrices)
        {
            _watchingPrices = IsWindowOpen;
            foreach (var coin in new[] { "BTC", "ETH" })
            {
                if (_watchingPrices)
                    _app.Watch(coin);
                else
                    _app.Unwatch(coin);
            }
        }
    }

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        if (NeedsNews && !_newsLoading && now - _newsAt >= NewsEvery)
            _ = LoadNewsAsync();
        if (NeedsFees && !_feesLoading && now - _feesAt >= FeesEvery)
            _ = LoadFeesAsync();
        var offset = DateTimeOffset.UtcNow;
        foreach (var h in Headlines)
            h.RefreshAge(offset);
        PruneLastHour();
        if (_feesAt != DateTime.MinValue)
            UpdateFeeCosts();
        Calendar.OnTick();
        Odds.OnTick();
    }

    // ---- News ---------------------------------------------------------------------------

    public ObservableCollection<HeadlineViewModel> Headlines { get; } = [];

    [ObservableProperty]
    private string _newsStatus = "";

    [ObservableProperty]
    private bool _newsOnlyMyCoins;

    partial void OnNewsOnlyMyCoinsChanged(bool value)
    {
        Settings.NewsOnlyMyCoins = value;
        _app.Save();
        ShowNews();
    }

    [ObservableProperty]
    private bool _notifyHeldCoinNews;

    partial void OnNotifyHeldCoinNewsChanged(bool value)
    {
        Settings.NotifyHeldCoinNews = value;
        _app.Save();
        UpdateDemand();
    }

    [RelayCommand]
    private Task ReloadNews() => LoadNewsAsync();

    private async Task LoadNewsAsync()
    {
        if (_newsLoading)
            return;
        _newsLoading = true;
        if (_allNews.Count == 0)
            NewsStatus = Loc.T("News.Loading");
        try
        {
            var (items, ok) = await _news.FetchAsync(TaggableCoins(), _lifetime.Token);
            _newsAt = DateTime.UtcNow;
            if (ok == 0)
            {
                NewsStatus = Loc.T("News.Offline");
                return;
            }
            var first = _seenUrls.Count == 0;
            var fresh = items.Where(i => _seenUrls.Add(i.Url)).ToList();
            _allNews = items;
            NewsStatus = ok < NewsService.Feeds.Count ? Loc.F("News.SomeFeeds", ok, NewsService.Feeds.Count) : "";
            ShowNews();
            if (!first)
                NotifyAboutHeldCoins(fresh);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _newsLoading = false;
        }
    }

    /// <summary>Every coin worth tagging: the well-known ones plus the user's own (DEX tokens too).</summary>
    private List<NewsCoin> TaggableCoins() =>
        MyCoins()
            .Concat(CoinCatalog.All.Select(c => c.Symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(k =>
            {
                var info = CoinCatalog.Get(k);
                return new NewsCoin(k, info.Symbol, info.Name);
            })
            .ToList();

    /// <summary>Coins on any widget or in the portfolio.</summary>
    public HashSet<string> MyCoins() =>
        new(_app.Widgets.SelectMany(w => w.Settings.Coins).Concat(_app.Settings.Holdings.Select(h => h.Symbol)), StringComparer.OrdinalIgnoreCase);

    private void ShowNews()
    {
        var mine = MyCoins();
        var list = NewsOnlyMyCoins ? _allNews.Where(i => i.Coins.Any(mine.Contains)) : _allNews;
        Headlines.Clear();
        foreach (var item in list.Take(60))
            Headlines.Add(new HeadlineViewModel(item));
        if (Headlines.Count == 0 && _allNews.Count > 0 && string.IsNullOrEmpty(NewsStatus))
            NewsStatus = Loc.T("News.NoneForMyCoins");
        else if (Headlines.Count > 0 && NewsStatus == Loc.T("News.NoneForMyCoins"))
            NewsStatus = "";
        HeadlinesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The latest headlines for a widget: ones about its own coins first.</summary>
    public IReadOnlyList<HeadlineViewModel> HeadlinesFor(IReadOnlyCollection<string> coins)
    {
        var set = new HashSet<string>(coins, StringComparer.OrdinalIgnoreCase);
        var recent = Headlines.Take(30).ToList();
        return recent.Where(h => h.Item.Coins.Any(set.Contains))
            .Concat(recent.Where(h => !h.Item.Coins.Any(set.Contains)))
            .Take(MaxWidgetHeadlines)
            .ToList();
    }

    private void NotifyAboutHeldCoins(List<NewsItem> fresh)
    {
        if (!NotifyHeldCoinNews || fresh.Count == 0)
            return;
        var held = new HashSet<string>(_app.Settings.Holdings.Select(h => h.Symbol), StringComparer.OrdinalIgnoreCase);
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(30);
        var hits = fresh.Where(i => i.Published >= cutoff && i.Coins.Any(held.Contains)).ToList();
        if (hits.Count == 0)
            return;
        var title = hits.Count == 1
            ? Loc.F("News.NotifyTitle", string.Join(", ", hits[0].Coins.Where(held.Contains).Select(CoinCatalog.DisplaySymbol)))
            : Loc.F("News.NotifyMany", hits.Count);
        NotificationRequested?.Invoke(title, string.Join("\n", hits.Take(3).Select(h => h.Title)));
    }

    // ---- Radar --------------------------------------------------------------------------

    public ObservableCollection<RadarEventViewModel> RadarEvents { get; } = [];

    /// <summary>Dollar sizes to pick from ("$500K"), as segmented buttons.</summary>
    public IReadOnlyList<ChoiceOption> WhaleOptions { get; private set; } = [];
    public IReadOnlyList<ChoiceOption> LiquidationOptions { get; private set; } = [];
    public IReadOnlyList<ChoiceOption> NotifyOptions { get; private set; } = [];

    private static List<ChoiceOption> Options(IReadOnlyList<decimal> values, decimal selected, Action<decimal> select) =>
        values.Select(v => new ChoiceOption(v.ToString(CultureInfo.InvariantCulture), () => RadarText.Usd(v), _ => select(v))
        {
            IsSelected = v == selected,
        }).ToList();

    private static void Mark(IReadOnlyList<ChoiceOption> options, decimal value)
    {
        foreach (var o in options)
            o.IsSelected = o.Value == value.ToString(CultureInfo.InvariantCulture);
    }

    [ObservableProperty]
    private decimal _minWhale;

    partial void OnMinWhaleChanged(decimal value)
    {
        Settings.MinWhaleUsd = value;
        Mark(WhaleOptions, value);
        _app.Save();
        RefilterRadar();
        if (_radarRunning)
            StartRadar();
    }

    [ObservableProperty]
    private decimal _minLiquidation;

    partial void OnMinLiquidationChanged(decimal value)
    {
        Settings.MinLiquidationUsd = value;
        Mark(LiquidationOptions, value);
        _app.Save();
        RefilterRadar();
        if (_radarRunning)
            StartRadar();
    }

    [ObservableProperty]
    private bool _radarOnlyMyCoins;

    partial void OnRadarOnlyMyCoinsChanged(bool value)
    {
        Settings.RadarOnlyMyCoins = value;
        _app.Save();
        RefilterRadar();
        if (_radarRunning)
            StartRadar();
    }

    [ObservableProperty]
    private bool _notifyRadar;

    partial void OnNotifyRadarChanged(bool value)
    {
        Settings.NotifyRadar = value;
        _app.Save();
        UpdateDemand();
        if (_radarRunning)
            StartRadar();
    }

    [ObservableProperty]
    private decimal _notifyRadarAt;

    partial void OnNotifyRadarAtChanged(decimal value)
    {
        Settings.NotifyRadarUsd = value;
        Mark(NotifyOptions, value);
        _app.Save();
        if (_radarRunning)
            StartRadar();
    }

    /// <summary>Which kinds the list shows: "All", "Whales" or "Liquidations".</summary>
    [ObservableProperty]
    private string _radarFilter = "All";

    partial void OnRadarFilterChanged(string value) => RefilterRadar();

    [RelayCommand]
    private void SetRadarFilter(string? filter) => RadarFilter = filter is "Whales" or "Liquidations" ? filter : "All";

    [ObservableProperty] private string _radarStatus = "";
    [ObservableProperty] private string _longsLiquidatedText = "$0K";
    [ObservableProperty] private string _shortsLiquidatedText = "$0K";
    [ObservableProperty] private string _whaleBuysText = "$0K";
    [ObservableProperty] private string _whaleSellsText = "$0K";

    private void StartRadar()
    {
        var mine = MyCoins().Where(k => !CoinKey.IsDex(k)).ToList();
        // Whale trades are watched coin by coin: the user's coins, or the big ones too.
        var whales = (RadarOnlyMyCoins ? mine : mine.Concat(CoinCatalog.All.Take(15).Select(c => c.Symbol)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
        if (whales.Count == 0)
            whales = ["BTC", "ETH", "SOL"];
        // Ask for the lowest of the shown and notified sizes, so notifications still work
        // when the list shows only bigger events.
        var minWhale = NotifyRadar ? Math.Min(MinWhale, NotifyRadarAt) : MinWhale;
        var minLiquidation = NotifyRadar ? Math.Min(MinLiquidation, NotifyRadarAt) : MinLiquidation;
        var config = $"{string.Join(",", whales)}|{minWhale}|{minLiquidation}";
        if (_radarRunning && config == _radarConfig)
            return;
        _radarConfig = config;
        _radarRunning = true;
        _radar.Start(whales, minWhale, minLiquidation);
        _radarFlush.Start();
        if (RadarEvents.Count == 0)
            RadarStatus = Loc.T("Radar.Listening");
    }

    private void StopRadar()
    {
        if (!_radarRunning)
            return;
        _radarRunning = false;
        _radarConfig = "";
        _radar.Stop();
        _radarFlush.Stop();
        _whaleSource = _liquidationSource = null;
        _liquidationSources.Clear();
    }

    private readonly ConcurrentDictionary<string, string> _liquidationSources = new();

    private void OnRadarSourceChanged(string part, string? name)
    {
        if (part == "whales")
            _whaleSource = name;
        else if (name is null)
            _liquidationSources.TryRemove(part, out _);
        else
            _liquidationSources[part] = name;
        _liquidationSource = _liquidationSources.IsEmpty ? null : string.Join(" + ", _liquidationSources.Values.Order());
    }

    [ObservableProperty]
    private string _radarSources = "";

    private void FlushRadar()
    {
        RadarSources = Loc.F("Radar.Sources", _whaleSource ?? "—", _liquidationSource ?? "—");
        if (_incoming.IsEmpty)
            return;
        var mine = MyCoins();
        var batch = new List<RadarEvent>();
        while (_incoming.TryDequeue(out var e))
            batch.Add(e);
        _lastHour.AddRange(batch);
        PruneLastHour();

        foreach (var e in batch.Where(e => Shows(e, mine)))
        {
            RadarEvents.Insert(0, new RadarEventViewModel(e));
            if (RadarEvents.Count > MaxRadarRows)
                RadarEvents.RemoveAt(RadarEvents.Count - 1);
        }
        if (RadarEvents.Count > 0)
            RadarStatus = "";
        UpdateRadarTotals(mine);
        NotifyAboutRadar(batch, mine);
    }

    private bool Shows(RadarEvent e, HashSet<string> mine) =>
        (!RadarOnlyMyCoins || mine.Contains(e.Symbol))
        && e.ValueUsd >= (e.IsWhale ? MinWhale : MinLiquidation)
        && RadarFilter switch
        {
            "Whales" => e.IsWhale,
            "Liquidations" => !e.IsWhale,
            _ => true,
        };

    private void RefilterRadar()
    {
        var mine = MyCoins();
        RadarEvents.Clear();
        foreach (var e in Enumerable.Reverse(_lastHour).Where(e => Shows(e, mine)).Take(MaxRadarRows))
            RadarEvents.Add(new RadarEventViewModel(e));
        UpdateRadarTotals(mine);
    }

    private void PruneLastHour()
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);
        _lastHour.RemoveAll(e => e.Time < cutoff);
        if (_lastHour.Count > 20_000)
            _lastHour.RemoveRange(0, _lastHour.Count - 20_000);
    }

    private void UpdateRadarTotals(HashSet<string> mine)
    {
        decimal Sum(RadarKind kind) => _lastHour
            .Where(e => e.Kind == kind && (!RadarOnlyMyCoins || mine.Contains(e.Symbol)) && e.ValueUsd >= (e.IsWhale ? MinWhale : MinLiquidation))
            .Sum(e => e.ValueUsd);
        LongsLiquidatedText = RadarText.Usd(Sum(RadarKind.LongLiquidated));
        ShortsLiquidatedText = RadarText.Usd(Sum(RadarKind.ShortLiquidated));
        WhaleBuysText = RadarText.Usd(Sum(RadarKind.WhaleBuy));
        WhaleSellsText = RadarText.Usd(Sum(RadarKind.WhaleSell));
    }

    private void NotifyAboutRadar(List<RadarEvent> batch, HashSet<string> mine)
    {
        if (!NotifyRadar || DateTime.UtcNow - _lastRadarNotify < TimeSpan.FromSeconds(30))
            return;
        var big = batch.Where(e => e.ValueUsd >= NotifyRadarAt && (!RadarOnlyMyCoins || mine.Contains(e.Symbol)))
            .OrderByDescending(e => e.ValueUsd)
            .FirstOrDefault();
        if (big is null)
            return;
        _lastRadarNotify = DateTime.UtcNow;
        var row = new RadarEventViewModel(big);
        NotificationRequested?.Invoke($"{row.Symbol}: {row.KindText} {row.ValueText}", row.DetailText);
    }

    // ---- Fees ---------------------------------------------------------------------------

    public FeeSummaryViewModel Fees { get; } = new();

    [ObservableProperty] private string _feeStatus = "";
    [ObservableProperty] private string _btcFastestText = "—";
    [ObservableProperty] private string _btcHalfHourText = "—";
    [ObservableProperty] private string _btcHourText = "—";
    [ObservableProperty] private string _btcEconomyText = "—";
    [ObservableProperty] private string _btcCostText = "";
    [ObservableProperty] private string _ethTotalText = "—";
    [ObservableProperty] private string _ethBaseText = "—";
    [ObservableProperty] private string _ethTipText = "—";
    [ObservableProperty] private string _ethCostText = "";

    private FeeSnapshot? _lastFees;

    /// <summary>Notify when gas is at or below this many gwei; empty is off.</summary>
    [ObservableProperty]
    private string _gasBelowText;

    partial void OnGasBelowTextChanged(string value)
    {
        Settings.EthGasBelowGwei = ParseLevel(value);
        _gasAlarm.Reset();
        _app.Save();
        UpdateDemand();
    }

    /// <summary>Notify when the fast Bitcoin fee is at or below this many sat/vB; empty is off.</summary>
    [ObservableProperty]
    private string _btcBelowText;

    partial void OnBtcBelowTextChanged(string value)
    {
        Settings.BtcFeeBelowSatVb = ParseLevel(value);
        _btcAlarm.Reset();
        _app.Save();
        UpdateDemand();
    }

    private static decimal? ParseLevel(string text) =>
        decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var v) && v is > 0 and <= 10_000 ? v : null;

    [RelayCommand]
    private Task ReloadFees() => LoadFeesAsync();

    private async Task LoadFeesAsync()
    {
        if (_feesLoading)
            return;
        _feesLoading = true;
        try
        {
            var snap = await _fees.FetchAsync(_lifetime.Token);
            _feesAt = DateTime.UtcNow;
            // Keep the last good half when one source is briefly down.
            _lastFees = new FeeSnapshot(snap.Bitcoin ?? _lastFees?.Bitcoin, snap.Ethereum ?? _lastFees?.Ethereum, snap.Time);
            FeeStatus = snap.Bitcoin is null && snap.Ethereum is null ? Loc.T("Fees.Offline") : "";
            ShowFees();
            CheckFeeAlarms(snap);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _feesLoading = false;
        }
    }

    private void ShowFees()
    {
        if (_lastFees is not { } f)
            return;
        var c = CultureInfo.CurrentCulture;
        string Sat(decimal v) => Loc.F("Fees.SatVb", v.ToString("0.#", c));
        string Gwei(decimal v) => Loc.F("Fees.Gwei", v.ToString(v < 1 ? "0.###" : v < 10 ? "0.##" : "0.#", c));
        if (f.Bitcoin is { } b)
        {
            BtcFastestText = Sat(b.Fastest);
            BtcHalfHourText = Sat(b.HalfHour);
            BtcHourText = Sat(b.Hour);
            BtcEconomyText = Sat(b.Economy);
        }
        if (f.Ethereum is { } e)
        {
            EthTotalText = Gwei(e.TotalGwei);
            EthBaseText = Gwei(e.BaseFeeGwei);
            EthTipText = Gwei(e.PriorityFeeGwei);
        }
        Fees.HasData = true;
        Fees.BitcoinText = f.Bitcoin is { } bb ? Sat(bb.Fastest) : "—";
        Fees.EthereumText = f.Ethereum is { } ee ? Gwei(ee.TotalGwei) : "—";
        UpdateFeeCosts();
    }

    /// <summary>What a typical payment costs now, in the user's currency when the price is known.</summary>
    private void UpdateFeeCosts()
    {
        if (_lastFees is not { } f)
            return;
        var quote = _app.Settings.QuoteCurrency;
        string Cost(string coin, decimal amount)
        {
            if (_app.Alerts.TryGetLatest(coin, out var price, out _) && price > 0)
                return CoinRowViewModel.FormatPrice(amount * price, quote);
            return amount.ToString(amount < 0.0001m ? "0.########" : "0.######", CultureInfo.CurrentCulture) + " " + coin;
        }
        BtcCostText = f.Bitcoin is { } b ? Loc.F("Fees.BtcCost", Cost("BTC", FeeService.BitcoinTxCostBtc(b.Fastest))) : "";
        EthCostText = f.Ethereum is { } e ? Loc.F("Fees.EthCost", Cost("ETH", e.TransferCostEth)) : "";
        Fees.ToolTip = string.Join("\n", new[]
        {
            Loc.T("Fees.Title"),
            f.Bitcoin is not null ? $"Bitcoin: {Fees.BitcoinText} · {BtcCostText}" : null,
            f.Ethereum is not null ? $"Ethereum: {Fees.EthereumText} · {EthCostText}" : null,
        }.OfType<string>());
    }

    private void CheckFeeAlarms(FeeSnapshot snap)
    {
        var c = CultureInfo.CurrentCulture;
        if (_gasAlarm.Check(Settings.EthGasBelowGwei, snap.Ethereum?.TotalGwei))
            NotificationRequested?.Invoke(Loc.F("Fees.GasAlertTitle", Settings.EthGasBelowGwei!.Value.ToString(c)), Loc.F("Fees.AlertBody", EthTotalText, EthCostText));
        if (_btcAlarm.Check(Settings.BtcFeeBelowSatVb, snap.Bitcoin?.Fastest))
            NotificationRequested?.Invoke(Loc.F("Fees.BtcAlertTitle", Settings.BtcFeeBelowSatVb!.Value.ToString(c)), Loc.F("Fees.AlertBody", BtcFastestText, BtcCostText));
    }

    // ---- Helpers ------------------------------------------------------------------------

    public void RefreshText()
    {
        OnTick();
        ShowFees();
        RefilterRadar();
        Calendar.RefreshText();
        Odds.RefreshText();
        RadarSources = Loc.F("Radar.Sources", _whaleSource ?? "—", _liquidationSource ?? "—");
    }

    /// <summary>"just now", "12m ago", "3h ago".</summary>
    public static string FormatAgo(TimeSpan age) =>
        age.TotalMinutes < 1 ? Loc.T("Ago.Now")
        : age.TotalHours < 1 ? Loc.F("Ago.Minutes", (int)age.TotalMinutes)
        : age.TotalDays < 1 ? Loc.F("Ago.Hours", (int)age.TotalHours)
        : Loc.F("Ago.Days", (int)age.TotalDays);

    /// <summary>Opens a web link in the default browser. Only https links are opened.</summary>
    public static void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CrashLog.Write(ex);
        }
    }

    private static decimal Closest(IReadOnlyList<decimal> options, decimal value) =>
        options.OrderBy(o => Math.Abs(o - value)).First();

    public void Dispose()
    {
        _lifetime.Cancel();
        Calendar.Stop();
        Odds.Stop();
        _tick.Stop();
        _radarFlush.Stop();
        _ = _radar.DisposeAsync();
    }
}
