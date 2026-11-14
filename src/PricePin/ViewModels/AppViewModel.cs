using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.History;
using PricePin.Core.Market;
using PricePin.Core.Settings;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>
/// App-wide state: the live price connection, the list of widgets, and the tray menu.
/// One connection serves every widget: it subscribes to the union of their coins, and
/// merged updates are handed to each widget about ten times a second.
/// </summary>
public sealed partial class AppViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan UiFlushInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan HistoryRefreshInterval = TimeSpan.FromMinutes(15);

    private readonly SettingsService _settings;
    private readonly MarketDataService _market;
    private readonly PriceHistoryService _history;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _restartTimer;
    private readonly Dictionary<string, Ticker> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime At, IReadOnlyList<decimal> Closes)> _historyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private FeedStatus _status = new(FeedState.Connecting, "—");
    private DateTimeOffset _lastTickAt;

    public AppViewModel(SettingsService settings, MarketDataService market, PriceHistoryService history, LogoProvider logos)
    {
        _settings = settings;
        _market = market;
        _history = history;
        Logos = logos;
        _dispatcher = Dispatcher.CurrentDispatcher;
        var s = settings.Current;

        // Trust the registry over the settings file: the user may have turned the
        // entry off in Task Manager. If it's on, refresh the path in case the exe moved.
        _startWithWindows = StartupService.IsEnabled();
        if (_startWithWindows)
            StartupService.SetEnabled(true);
        s.StartWithWindows = _startWithWindows;

        DataSourceOptions =
        [
            new(DataSource.Auto, "Auto", "Source.Auto", "Source.AutoDesc", SelectDataSource),
            new(DataSource.Binance, "Binance", null, "Source.BinanceDesc", SelectDataSource),
            new(DataSource.Coinbase, "Coinbase", null, "Source.CoinbaseDesc", SelectDataSource),
            new(DataSource.Kraken, "Kraken", null, "Source.KrakenDesc", SelectDataSource),
        ];
        foreach (var o in DataSourceOptions)
            o.IsSelected = o.Value == s.DataSource;

        LanguageOptions =
        [
            new(AppSettings.SystemLanguage, null, SelectLanguage),
            .. Loc.Languages.Select(l => new LanguageOption(l.Code, l.NativeName, SelectLanguage)),
        ];
        foreach (var o in LanguageOptions)
            o.IsSelected = o.Code == s.Language;
        if (!LanguageOptions.Any(o => o.IsSelected))
            LanguageOptions[0].IsSelected = true;
        Loc.Changed += OnLanguageChanged;

        _hotkeyToggleWidgets = s.Hotkeys.ToggleWidgets;
        _hotkeyToggleClickThrough = s.Hotkeys.ToggleClickThrough;

        Widgets = new(s.Widgets.Select(w => new WidgetViewModel(this, w)));
        Alerts = new AlertsViewModel(this);
        _trayCoin = s.TrayCoin;
        RebuildTrayCoinOptions();

        _market.TickReceived += OnTickReceived;
        _market.StatusChanged += OnStatusChanged;

        _restartTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) =>
        {
            _restartTimer!.Stop();
            RestartFeed();
        }, _dispatcher);
        _restartTimer.Stop();
        _flushTimer = new DispatcherTimer(UiFlushInterval, DispatcherPriority.Render, (_, _) => FlushTicks(), _dispatcher);
        _clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshStatusText(), _dispatcher);
    }

    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;

    public LogoProvider Logos { get; }
    public AppSettings Settings => _settings.Current;
    public ObservableCollection<WidgetViewModel> Widgets { get; }
    public IReadOnlyList<DataSourceOption> DataSourceOptions { get; }
    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public AlertsViewModel Alerts { get; }

    /// <summary>Coins the tray icon can show ("" is the PricePin logo).</summary>
    public ObservableCollection<ChoiceOption> TrayCoinOptions { get; } = [];

    /// <summary>Coin drawn in the tray icon; empty for the logo.</summary>
    [ObservableProperty]
    private string _trayCoin;

    /// <summary>Short price for the tray icon ("97k"), or null to show the logo.</summary>
    [ObservableProperty]
    private string? _trayIconText;

    /// <summary>Colour of <see cref="TrayIconText"/>: up (green) or down (red) over 24h.</summary>
    [ObservableProperty]
    private bool _trayIconUp = true;

    /// <summary>Page the next Settings window opens on, e.g. "Alerts".</summary>
    public string? PendingSettingsPage { get; set; }

    /// <summary>Background updates; set by the app at startup.</summary>
    public UpdatesViewModel? Updates { get; set; }

    /// <summary>"Turn it off again here, from the tray icon, or with Ctrl+Alt+T."</summary>
    public string ClickThroughOffHint =>
        Loc.F("Widgets.ClickThroughHint", string.IsNullOrWhiteSpace(HotkeyToggleClickThrough) ? "—" : HotkeyToggleClickThrough);

    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>Shortcut that shows or hides every widget, e.g. "Ctrl+Alt+P".</summary>
    [ObservableProperty]
    private string _hotkeyToggleWidgets = "";

    /// <summary>Shortcut that toggles click-through on every widget.</summary>
    [ObservableProperty]
    private string _hotkeyToggleClickThrough = "";

    /// <summary>Set when a shortcut couldn't be registered (usually another app owns it).</summary>
    [ObservableProperty]
    private string? _hotkeyWarning;

    /// <summary>Raised when a shortcut changed and needs registering again.</summary>
    public event EventHandler? HotkeysChanged;

    partial void OnHotkeyToggleWidgetsChanged(string value)
    {
        Settings.Hotkeys.ToggleWidgets = value;
        Save();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnHotkeyToggleClickThroughChanged(string value)
    {
        Settings.Hotkeys.ToggleClickThrough = value;
        OnPropertyChanged(nameof(ClickThroughOffHint));
        Save();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    private FeedState _statusState = FeedState.Connecting;

    [ObservableProperty]
    private string _statusLabel = "";

    [ObservableProperty]
    private string _statusSource = "";

    [ObservableProperty]
    private string _statusDetail = "";

    [ObservableProperty]
    private string _trayToolTip = "PricePin";

    /// <summary>True if any widget is shown; drives the tray's Show/Hide item.</summary>
    public bool AnyWidgetVisible => Widgets.Any(w => w.IsWidgetVisible);

    /// <summary>True if any widget ignores the mouse; drives the tray's click-through item.</summary>
    public bool AnyClickThrough => Widgets.Any(w => w.ClickThrough);

    public void Start()
    {
        ApplyStatusLabel();
        RefreshStatusText();
        _flushTimer.Start();
        _clockTimer.Start();
        foreach (var w in Widgets)
            w.ApplyCoinSettings();
        RestartFeed();
        _ = RefreshHistoryLoopAsync(_lifetime.Token);
        _ = Alerts.LoadLogosAsync();
    }

    public void Save() => _settings.ScheduleSave();

    /// <summary>Called by widgets when their coins change; merges quick edits into one reconnect.</summary>
    public void RequestFeedRestart()
    {
        _restartTimer.Stop();
        _restartTimer.Start();
    }

    private HashSet<string> _subscribed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reconnects only if <paramref name="symbol"/> isn't streaming yet (e.g. a new alert's coin).</summary>
    public void EnsureSubscribed(string symbol)
    {
        if (!_subscribed.Contains(symbol))
            RequestFeedRestart();
    }

    /// <summary>Re-reads the currency for every widget (prices start fresh).</summary>
    public void ApplyQuoteCurrency()
    {
        lock (_pending)
            _pending.Clear();
        _historyCache.Clear();
        foreach (var w in Widgets)
            w.ApplyCoinSettings(currencyChanged: true);
        Alerts.OnQuoteChanged();
        TrayIconText = null;
        RequestFeedRestart();
    }

    internal void RaiseWidgetStateChanged()
    {
        OnPropertyChanged(nameof(AnyWidgetVisible));
        OnPropertyChanged(nameof(AnyClickThrough));
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!StartupService.SetEnabled(value))
        {
            StartWithWindows = StartupService.IsEnabled();
            return;
        }
        Settings.StartWithWindows = value;
        Save();
    }

    // ---- Commands ----------------------------------------------------------------------

    [RelayCommand]
    public void ToggleAllWidgets()
    {
        var show = !AnyWidgetVisible;
        foreach (var w in Widgets)
            w.IsWidgetVisible = show;
    }

    [RelayCommand]
    public void ToggleClickThroughAll()
    {
        var enable = !AnyClickThrough;
        foreach (var w in Widgets)
            w.ClickThrough = enable;
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenAlerts()
    {
        PendingSettingsPage = "Alerts";
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Reconnect() => RestartFeed();

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    public WidgetViewModel? AddWidget(WidgetLayout layout)
    {
        if (Widgets.Count >= AppSettings.MaxWidgets)
            return null;

        var cfg = new WidgetSettings
        {
            Name = layout == WidgetLayout.TickerBar ? Loc.T("Widgets.Bar") : Loc.F("Widget.DefaultName", Widgets.Count + 1),
            Layout = layout,
            Coins = Widgets.FirstOrDefault()?.Settings.Coins.ToList() ?? [.. Core.Coins.CoinCatalog.Defaults],
        };
        Settings.Widgets.Add(cfg);
        var vm = new WidgetViewModel(this, cfg);
        Widgets.Add(vm);
        vm.ApplyCoinSettings();
        RequestFeedRestart();
        Save();
        RaiseWidgetStateChanged();
        return vm;
    }

    public void RemoveWidget(WidgetViewModel widget)
    {
        if (Widgets.Count <= 1)
            return; // always keep one
        Widgets.Remove(widget);
        Settings.Widgets.Remove(widget.Settings);
        RequestFeedRestart();
        Save();
        RaiseWidgetStateChanged();
    }

    private void SelectDataSource(DataSourceOption option)
    {
        foreach (var o in DataSourceOptions)
            o.IsSelected = o == option;
        Settings.DataSource = option.Value;
        Save();
        RestartFeed();
    }

    private void SelectLanguage(LanguageOption option)
    {
        foreach (var o in LanguageOptions)
            o.IsSelected = o == option;
        Settings.Language = option.Code;
        Save();
        Loc.Apply(option.Code);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var o in DataSourceOptions)
            o.RefreshText();
        foreach (var o in LanguageOptions)
            o.RefreshText();
        OnPropertyChanged(nameof(ClickThroughOffHint));
        ApplyStatusLabel();
        RefreshStatusText();
        foreach (var w in Widgets)
            w.RefreshText();
        Updates?.RefreshText();
        Alerts.RefreshText();
        foreach (var o in TrayCoinOptions)
            o.RefreshText();
    }

    partial void OnTrayCoinChanged(string value)
    {
        Settings.TrayCoin = value;
        Save();
        foreach (var o in TrayCoinOptions)
            o.IsSelected = o.Value == value;
        UpdateTrayIcon();
    }

    private void RebuildTrayCoinOptions()
    {
        var coins = Widgets.SelectMany(w => w.Settings.Coins).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var values = new List<string> { "" };
        values.AddRange(coins);
        if (TrayCoinOptions.Select(o => o.Value).SequenceEqual(values))
            return;
        TrayCoinOptions.Clear();
        foreach (var v in values)
        {
            var symbol = v;
            TrayCoinOptions.Add(new ChoiceOption(v, () => symbol.Length == 0 ? Loc.T("Tray.Logo") : symbol, o => TrayCoin = o.Value)
            {
                IsSelected = v == TrayCoin,
            });
        }
    }

    private void UpdateTrayIcon()
    {
        if (TrayCoin.Length > 0 && Alerts.TryGetLatest(TrayCoin, out var price, out var change))
        {
            TrayIconText = PriceText.Tiny(price);
            TrayIconUp = (change ?? 0) >= 0;
        }
        else
            TrayIconText = null;
    }

    private void RestartFeed()
    {
        var quote = Settings.QuoteCurrency;
        RebuildTrayCoinOptions();
        var symbols = Widgets
            .SelectMany(w => w.Settings.Coins)
            .Concat(Alerts.WatchedSymbols)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(c => new MarketSymbol(c, quote))
            .ToList();
        _subscribed = new(symbols.Select(m => m.Base), StringComparer.OrdinalIgnoreCase);
        _market.Start(symbols, Settings.DataSource);
    }

    // ---- Live data ---------------------------------------------------------------------

    private void OnTickReceived(Ticker t)
    {
        lock (_pending)
            _pending[t.Base] = _pending.TryGetValue(t.Base, out var prev) ? Merge(prev, t) : t;
    }

    /// <summary>Newest price wins; 24h stats are kept from whichever update had them.</summary>
    internal static Ticker Merge(Ticker older, Ticker newer) => newer with
    {
        Open24h = newer.Open24h ?? older.Open24h,
        High24h = newer.High24h ?? older.High24h,
        Low24h = newer.Low24h ?? older.Low24h,
        Volume24h = newer.Volume24h ?? older.Volume24h,
        ChangePercent24h = newer.ChangePercent24h ?? older.ChangePercent24h,
    };

    private void FlushTicks()
    {
        Ticker[] batch;
        lock (_pending)
        {
            if (_pending.Count == 0)
                return;
            batch = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var w in Widgets)
            w.Apply(batch);
        Alerts.OnPrices(batch);
        UpdateTrayIcon();
        _lastTickAt = DateTimeOffset.UtcNow;
    }

    private void OnStatusChanged(FeedStatus status) =>
        _dispatcher.BeginInvoke(() =>
        {
            _status = status;
            StatusState = status.State;
            StatusSource = status.State is FeedState.Offline ? "" : status.Source;
            ApplyStatusLabel();
            RefreshStatusText();
        });

    private void ApplyStatusLabel() =>
        StatusLabel = Loc.T(_status.State switch
        {
            FeedState.Live => "Status.Live",
            FeedState.Polling => "Status.Delayed",
            FeedState.Connecting => "Status.Connecting",
            FeedState.Reconnecting => "Status.Reconnecting",
            _ => "Status.Offline",
        });

    private void RefreshStatusText()
    {
        var now = DateTimeOffset.UtcNow;
        var retry = _status.RetryAt is { } at && at > now ? Loc.F("Status.Retry", FormatAge(at - now)) : "";
        var age = _lastTickAt == default ? "" : Loc.F("Status.Age", FormatAge(now - _lastTickAt));

        var detail = _status.State switch
        {
            FeedState.Live => Loc.F("Status.LiveDetail", _status.Source, age),
            FeedState.Polling => Loc.F("Status.PollingDetail", _status.Source, age),
            FeedState.Connecting => Loc.F("Status.ConnectingDetail", _status.Source),
            FeedState.Reconnecting => Loc.F("Status.ReconnectingDetail", _status.Source, retry),
            _ when !Widgets.Any(w => w.Settings.Coins.Count > 0) => Loc.T("Status.NoCoins"),
            _ => Loc.F("Status.OfflineDetail", retry),
        };
        // The exchange's own error text (technical, in English) helps when reporting problems.
        if (_status.State is FeedState.Reconnecting && !string.IsNullOrWhiteSpace(_status.Detail))
            detail += "\n" + _status.Detail;
        StatusDetail = detail;

        var rows = Widgets.SelectMany(w => w.Coins).ToList();
        if ((rows.FirstOrDefault(r => r.Symbol == TrayCoin) ?? rows.FirstOrDefault()) is { HasPrice: true } first)
            TrayToolTip = $"PricePin · {first.Symbol} {first.PriceText} {first.ChangeText}";
    }

    private static string FormatAge(TimeSpan t) =>
        t.TotalSeconds < 1 ? Loc.T("Time.UnderSecond")
        : t.TotalMinutes < 1 ? Loc.F("Time.Seconds", Math.Ceiling(t.TotalSeconds))
        : Loc.F("Time.Minutes", Math.Floor(t.TotalMinutes));

    // ---- Logos and history (shared by every widget) -------------------------------------

    internal async Task LoadLogosAsync(IReadOnlyList<CoinRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            if (row.Logo is not null)
                continue;
            try
            {
                row.Logo = await Logos.GetAsync(row.Symbol, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task LoadHistoryAsync(IReadOnlyList<CoinRowViewModel> rows, bool forceRefresh = false)
    {
        var quote = Settings.QuoteCurrency;
        try
        {
            foreach (var row in rows)
            {
                if (!forceRefresh && _historyCache.TryGetValue(row.Symbol, out var hit) && DateTime.UtcNow - hit.At < HistoryRefreshInterval)
                {
                    row.SetHistory(hit.Closes);
                    continue;
                }

                var closes = await _history.GetLast24hAsync(new MarketSymbol(row.Symbol, quote), _lifetime.Token);
                if (closes.Count == 0)
                    continue;
                _historyCache[row.Symbol] = (DateTime.UtcNow, closes);
                foreach (var same in Widgets.SelectMany(w => w.Coins).Where(r => r.Symbol == row.Symbol))
                    same.SetHistory(closes);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task RefreshHistoryLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(HistoryRefreshInterval, ct);
                var unique = Widgets.SelectMany(w => w.Coins).DistinctBy(r => r.Symbol).ToList();
                await LoadHistoryAsync(unique, forceRefresh: true);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _flushTimer.Stop();
        _clockTimer.Stop();
        _restartTimer.Stop();
        _market.TickReceived -= OnTickReceived;
        _market.StatusChanged -= OnStatusChanged;
        Loc.Changed -= OnLanguageChanged;
    }

    internal static string Percent(double value) => Math.Round(value * 100).ToString(CultureInfo.InvariantCulture) + "%";
}
