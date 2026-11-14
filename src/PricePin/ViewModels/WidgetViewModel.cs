using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Coins;
using PricePin.Core.History;
using PricePin.Core.Market;
using PricePin.Models;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>
/// State behind the widget, its right-click menu and the tray menu. Prices arrive on a
/// background thread and are applied to the rows in batches about ten times a second,
/// which looks instant but keeps CPU use low even when a coin trades hundreds of times a second.
/// </summary>
public sealed partial class WidgetViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan UiFlushInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan HistoryRefreshInterval = TimeSpan.FromMinutes(15);

    private readonly SettingsService _settings;
    private readonly MarketDataService _market;
    private readonly PriceHistoryService _history;
    private readonly LogoProvider _logos;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _restartTimer;
    private string _activeQuote = "";
    private readonly Dictionary<string, Ticker> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CoinRowViewModel> _rowsBySymbol = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private FeedStatus _status = new(FeedState.Connecting, "—");
    private DateTimeOffset _lastTickAt;

    public WidgetViewModel(SettingsService settings, MarketDataService market, PriceHistoryService history, LogoProvider logos)
    {
        _settings = settings;
        _market = market;
        _history = history;
        _logos = logos;
        _dispatcher = Dispatcher.CurrentDispatcher;
        var s = settings.Current;

        _topmost = s.Topmost;
        _opacity = s.Opacity;
        _clickThrough = s.ClickThrough;
        _lockPosition = s.LockPosition;
        _snapToEdges = s.SnapToEdges;

        // Trust the registry over the settings file: the user may have turned the
        // entry off in Task Manager. If it's on, refresh the path in case the exe moved.
        _startWithWindows = StartupService.IsEnabled();
        if (_startWithWindows)
            StartupService.SetEnabled(true);
        s.StartWithWindows = _startWithWindows;

        _size = s.Size;
        _showNames = s.ShowNames;
        _showSparklines = s.ShowSparklines;
        _showColumnHeadings = s.ShowColumnHeadings;

        Coins = [];

        DataSourceOptions =
        [
            new(DataSource.Auto, "Automatic (best available)",
                "Tries Binance, then Coinbase, then Kraken, and switches by itself if one is down or blocked where you are.", SelectDataSource),
            new(DataSource.Binance, "Binance", "Largest choice of coins. Not available in some countries.", SelectDataSource),
            new(DataSource.Coinbase, "Coinbase", "Real USD prices. Available almost everywhere.", SelectDataSource),
            new(DataSource.Kraken, "Kraken", "Real USD and EUR prices. Available almost everywhere.", SelectDataSource),
        ];
        foreach (var o in DataSourceOptions)
            o.IsSelected = o.Value == s.DataSource;

        _market.TickReceived += OnTickReceived;
        _market.StatusChanged += OnStatusChanged;

        // Coalesce quick edits in the coin picker into one reconnect.
        _restartTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) =>
        {
            _restartTimer!.Stop();
            RestartFeed();
        }, _dispatcher);
        _restartTimer.Stop();

        _flushTimer = new DispatcherTimer(UiFlushInterval, DispatcherPriority.Render, (_, _) => FlushTicks(), _dispatcher);
        _clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshStatusText(), _dispatcher);
    }

    /// <summary>Raised when the user picks Exit.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Raised when the user asks for the settings window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the user asks to show or hide the widget.</summary>
    public event EventHandler? ToggleVisibilityRequested;

    public ObservableCollection<CoinRowViewModel> Coins { get; }

    public IReadOnlyList<DataSourceOption> DataSourceOptions { get; }

    public double OpacityMin => OpacityLimits.Min;
    public double OpacityMax => OpacityLimits.Max;

    public string OpacityText => Math.Round(Opacity * 100).ToString(CultureInfo.InvariantCulture) + "%";

    [ObservableProperty]
    private bool _topmost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityText))]
    private double _opacity;

    [ObservableProperty]
    private bool _clickThrough;

    [ObservableProperty]
    private bool _lockPosition;

    [ObservableProperty]
    private bool _snapToEdges;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _isWidgetVisible = true;

    // ---- Appearance --------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Scale))]
    private WidgetSize _size;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardWidth))]
    private bool _showNames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardWidth))]
    private bool _showSparklines;

    [ObservableProperty]
    private bool _showColumnHeadings;

    /// <summary>Zoom applied to the whole card.</summary>
    public double Scale => Size switch
    {
        WidgetSize.Small => 0.86,
        WidgetSize.Large => 1.18,
        _ => 1.0,
    };

    /// <summary>Card width before scaling; narrower when columns are hidden.</summary>
    public double CardWidth => 320 - (ShowSparklines ? 0 : 64) - (ShowNames ? 0 : 36);

    partial void OnSizeChanged(WidgetSize value) => Save(s => s.Size = value);
    partial void OnShowNamesChanged(bool value) => Save(s => s.ShowNames = value);
    partial void OnShowSparklinesChanged(bool value) => Save(s => s.ShowSparklines = value);
    partial void OnShowColumnHeadingsChanged(bool value) => Save(s => s.ShowColumnHeadings = value);

    /// <summary>Connection state for the status pill.</summary>
    [ObservableProperty]
    private FeedState _statusState = FeedState.Connecting;

    /// <summary>Short pill text, e.g. "LIVE".</summary>
    [ObservableProperty]
    private string _statusLabel = "CONNECTING";

    /// <summary>Feed name next to the pill, e.g. "Binance".</summary>
    [ObservableProperty]
    private string _statusSource = "";

    /// <summary>Longer explanation shown on hover.</summary>
    [ObservableProperty]
    private string _statusDetail = "Connecting…";

    /// <summary>Tray icon tooltip, e.g. "PricePin · BTC $97,412.35".</summary>
    [ObservableProperty]
    private string _trayToolTip = "PricePin";

    /// <summary>Starts live prices, logos and history. Call once the UI is up.</summary>
    public void Start()
    {
        _flushTimer.Start();
        _clockTimer.Start();
        ApplyCoinSettings(restartNow: true);
        _ = RefreshHistoryLoopAsync(_lifetime.Token);
    }

    /// <summary>
    /// Rebuilds the rows after the coin list or currency changed in Settings. Rows for
    /// coins that stay keep their price and sparkline; new ones load logo and history.
    /// </summary>
    public void ApplyCoinSettings(bool restartNow = false)
    {
        var s = _settings.Current;
        if (!string.Equals(_activeQuote, s.QuoteCurrency, StringComparison.Ordinal))
        {
            // Prices in another currency: start every row fresh.
            _activeQuote = s.QuoteCurrency;
            _rowsBySymbol.Clear();
            lock (_pending)
                _pending.Clear();
        }

        var added = new List<CoinRowViewModel>();
        var rows = new List<CoinRowViewModel>();
        foreach (var symbol in s.Coins)
        {
            if (!_rowsBySymbol.TryGetValue(symbol, out var row))
            {
                row = new CoinRowViewModel(CoinCatalog.Get(symbol), s.QuoteCurrency) { Logo = _logos.GetLocal(symbol) };
                added.Add(row);
            }
            rows.Add(row);
        }

        _rowsBySymbol.Clear();
        Coins.Clear();
        foreach (var row in rows)
        {
            _rowsBySymbol[row.Symbol] = row;
            Coins.Add(row);
        }

        if (restartNow)
            RestartFeed();
        else
        {
            _restartTimer.Stop();
            _restartTimer.Start();
        }

        _ = LoadLogosAsync(added, _lifetime.Token);
        _ = LoadHistoryAsync(added, _lifetime.Token);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _flushTimer.Stop();
        _clockTimer.Stop();
        _market.TickReceived -= OnTickReceived;
        _market.StatusChanged -= OnStatusChanged;
    }

    partial void OnTopmostChanged(bool value) => Save(s => s.Topmost = value);

    partial void OnOpacityChanged(double value)
    {
        var clamped = double.IsFinite(value) ? Math.Clamp(value, OpacityLimits.Min, OpacityLimits.Max) : OpacityLimits.Max;
        if (clamped != value)
        {
            Opacity = clamped; // re-enters with the clamped value, which then gets saved
            return;
        }
        Save(s => s.Opacity = value);
    }

    partial void OnClickThroughChanged(bool value) => Save(s => s.ClickThrough = value);

    partial void OnLockPositionChanged(bool value) => Save(s => s.LockPosition = value);

    partial void OnSnapToEdgesChanged(bool value) => Save(s => s.SnapToEdges = value);

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!StartupService.SetEnabled(value))
        {
            // Couldn't write the registry entry; show the real state again.
            StartWithWindows = StartupService.IsEnabled();
            return;
        }
        Save(s => s.StartWithWindows = value);
    }

    public void SavePosition(double left, double top) => Save(s =>
    {
        s.Left = left;
        s.Top = top;
    });

    private void SelectDataSource(DataSourceOption option)
    {
        foreach (var o in DataSourceOptions)
            o.IsSelected = o == option;
        Save(s => s.DataSource = option.Value);
        RestartFeed();
    }

    [RelayCommand]
    private void Reconnect() => RestartFeed();

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleVisibility() => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void RestartFeed()
    {
        var s = _settings.Current;
        var symbols = s.Coins.Select(c => new MarketSymbol(c, s.QuoteCurrency)).ToList();
        _market.Start(symbols, s.DataSource);
    }

    private void Save(Action<AppSettings> apply)
    {
        apply(_settings.Current);
        _settings.ScheduleSave();
    }

    // ---- Live data ---------------------------------------------------------------------

    /// <summary>Background thread. Merges updates so each coin gets at most one per UI flush.</summary>
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

        foreach (var t in batch)
        {
            if (_rowsBySymbol.TryGetValue(t.Base, out var row))
                row.Apply(t);
        }
        _lastTickAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Background thread.</summary>
    private void OnStatusChanged(FeedStatus status) =>
        _dispatcher.BeginInvoke(() =>
        {
            _status = status;
            StatusState = status.State;
            StatusSource = status.State is FeedState.Offline ? "" : status.Source;
            StatusLabel = status.State switch
            {
                FeedState.Live => "LIVE",
                FeedState.Polling => "DELAYED",
                FeedState.Connecting => "CONNECTING",
                FeedState.Reconnecting => "RECONNECTING",
                _ => "OFFLINE",
            };
            RefreshStatusText();
        });

    /// <summary>Runs every second: tooltip countdowns, freshness and the tray tooltip.</summary>
    private void RefreshStatusText()
    {
        var now = DateTimeOffset.UtcNow;
        var retry = _status.RetryAt is { } at && at > now ? $" Retrying in {Math.Ceiling((at - now).TotalSeconds):0}s." : "";
        var age = _lastTickAt == default ? "" : $" Last update {FormatAge(now - _lastTickAt)} ago.";

        StatusDetail = _status.State switch
        {
            FeedState.Live => $"Streaming live from {_status.Source}.{age}",
            FeedState.Polling => $"Streaming isn't available right now, so prices are fetched from {_status.Source} every few seconds.{age}",
            FeedState.Connecting => $"Connecting to {_status.Source}…",
            FeedState.Reconnecting => $"{_status.Detail ?? "Connection lost"}.{retry}",
            _ => $"{_status.Detail ?? "Offline"}.{retry} Check your internet connection.",
        };

        if (Coins.FirstOrDefault() is { HasPrice: true } first)
            TrayToolTip = $"PricePin · {first.Symbol} {first.PriceText} {first.ChangeText}";
    }

    private static string FormatAge(TimeSpan t) =>
        t.TotalSeconds < 1 ? "<1s" : t.TotalMinutes < 1 ? $"{t.TotalSeconds:0}s" : $"{t.TotalMinutes:0}m";

    // ---- Logos and history -------------------------------------------------------------

    private async Task LoadLogosAsync(IReadOnlyList<CoinRowViewModel> rows, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            if (row.Logo is not null)
                continue;
            try
            {
                row.Logo = await _logos.GetAsync(row.Symbol, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task LoadHistoryAsync(IReadOnlyList<CoinRowViewModel> rows, CancellationToken ct)
    {
        var quote = _settings.Current.QuoteCurrency;
        try
        {
            foreach (var row in rows)
            {
                var closes = await _history.GetLast24hAsync(new MarketSymbol(row.Symbol, quote), ct);
                if (closes.Count > 0)
                    row.SetHistory(closes);
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
                await LoadHistoryAsync(Coins.ToList(), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
