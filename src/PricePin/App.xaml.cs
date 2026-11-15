using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using PricePin.Core.Coins;
using PricePin.Core.History;
using PricePin.Core.Icons;
using PricePin.Core.Market;
using PricePin.Core.Settings;
using PricePin.Services;
using PricePin.ViewModels;
using PricePin.Views;

namespace PricePin;

public partial class App : Application
{
    // Unique per user session; a second launch signals the first one to show itself and quits.
    private const string InstanceId = "PricePin.SingleInstance.5B0E4D8C";

    private readonly Dictionary<WidgetViewModel, Window> _windows = [];
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private SettingsService? _settings;
    private MarketDataService? _market;
    private ThemeService? _theme;
    private LogoProvider? _logos;
    private CoinCatalogService? _catalog;
    private HotkeyService? _hotkeys;
    private UpdatesViewModel? _updates;
    private AppViewModel? _vm;
    private TaskbarIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private MarketWindow? _marketWindow;
    private QuickSearchWindow? _quickSearch;
    private readonly Dictionary<string, CoinDetailWindow> _detailWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly CandleService _candles = new();
    private readonly MarketOverviewService _overview = new();
    private bool _clickThroughHintShown;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => CrashLog.Write(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write(args.ExceptionObject as Exception);

        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\" + InstanceId, out var isFirst);
        if (!isFirst)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(@"Local\" + InstanceId + ".Show");
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // First instance is still starting up; nothing to signal.
            }
            Shutdown();
            return;
        }
        ListenForShowSignal();

        _settings = new SettingsService();
        _theme = new ThemeService(Resources);
        _theme.Apply(_settings.Current.Theme, _settings.Current.Accent);
        Loc.Apply(_settings.Current.Language);

        _market = new MarketDataService();
        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PricePin");
        _logos = new LogoProvider(new CoinIconService(Path.Combine(localData, "icons")));
        _catalog = new CoinCatalogService(Path.Combine(localData, "catalog"));

        _vm = new AppViewModel(_settings, _market, new PriceHistoryService(), _logos);
        _vm.ExitRequested += (_, _) => ExitApp();
        _vm.SettingsRequested += (_, _) => ShowSettings();
        _vm.HotkeysChanged += (_, _) => RegisterHotkeys();
        _vm.Widgets.CollectionChanged += OnWidgetsChanged;

        _updates = new UpdatesViewModel(new UpdateService(), _vm);
        _updates.ReadyToRestart += (_, _) => _tray?.ShowNotification(
            Loc.F("Update.NotifyTitle", _updates.NewVersion), Loc.T("Update.NotifyBody"), NotificationIcon.Info);
        _updates.RestartRequested += (_, _) => RestartForUpdate();
        _vm.Updates = _updates;
        _vm.Alerts.Triggered += (_, hits) => ShowAlert(hits);
        _vm.CoinDetailRequested += (_, symbol) => ShowCoinDetail(symbol);
        _vm.MarketRequested += (_, _) => ShowMarket();
        _vm.QuickSearchRequested += (_, _) => ToggleQuickSearch();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppViewModel.TrayIconText) or nameof(AppViewModel.TrayIconUp))
                UpdateTrayIcon();
        };

        CreateTrayIcon();

        _hotkeys = new HotkeyService();
        RegisterHotkeys();

        foreach (var widget in _vm.Widgets)
            Attach(widget);

        _vm.Start();
        _updates.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose();
        _updates?.Dispose();
        _hotkeys?.Dispose();
        _theme?.Dispose();
        // Close sockets politely, but never let shutdown hang on the network.
        _market?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _settings?.SaveNow();
        _tray?.Dispose();
        _showSignal?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    // ---- Widget windows ----------------------------------------------------------------

    private void Attach(WidgetViewModel widget)
    {
        widget.PropertyChanged += OnWidgetPropertyChanged;
        widget.LayoutChanged += OnWidgetLayoutChanged;
        if (widget.IsWidgetVisible)
            ShowWindowFor(widget);
    }

    private void Detach(WidgetViewModel widget)
    {
        widget.PropertyChanged -= OnWidgetPropertyChanged;
        widget.LayoutChanged -= OnWidgetLayoutChanged;
        CloseWindowFor(widget);
    }

    private void OnWidgetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (WidgetViewModel w in e.OldItems ?? Array.Empty<WidgetViewModel>())
            Detach(w);
        foreach (WidgetViewModel w in e.NewItems ?? Array.Empty<WidgetViewModel>())
            Attach(w);
    }

    private void OnWidgetPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not WidgetViewModel widget)
            return;

        switch (e.PropertyName)
        {
            case nameof(WidgetViewModel.IsWidgetVisible):
                if (widget.IsWidgetVisible)
                    ShowWindowFor(widget);
                else if (_windows.TryGetValue(widget, out var window))
                    window.Hide();
                break;

            case nameof(WidgetViewModel.ClickThrough) when widget.ClickThrough && !_clickThroughHintShown:
                // Click-through makes the widget ignore the mouse, so point people to the way back once.
                _clickThroughHintShown = true;
                _tray?.ShowNotification(
                    Loc.T("Notify.ClickThroughTitle"),
                    Loc.F("Notify.ClickThroughBody", _vm!.HotkeyToggleClickThrough),
                    NotificationIcon.Info);
                break;
        }
    }

    private void OnWidgetLayoutChanged(object? sender, EventArgs e)
    {
        if (sender is not WidgetViewModel widget)
            return;
        CloseWindowFor(widget);
        if (widget.IsWidgetVisible)
            ShowWindowFor(widget);
    }

    private void ShowWindowFor(WidgetViewModel widget)
    {
        if (!_windows.TryGetValue(widget, out var window))
        {
            var cascade = 24.0 * _vm!.Widgets.IndexOf(widget);
            window = widget.IsTickerBar ? new TickerBarWindow(widget) : new WidgetWindow(widget, cascade);
            _windows[widget] = window;
        }
        window.Show();
    }

    private void CloseWindowFor(WidgetViewModel widget)
    {
        if (!_windows.Remove(widget, out var window))
            return;
        switch (window)
        {
            case WidgetWindow w: w.CloseForExit(); break;
            case TickerBarWindow t: t.CloseForExit(); break;
        }
    }

    // ---- Tray, settings, hotkeys -------------------------------------------------------

    private void CreateTrayIcon()
    {
        var menu = (ContextMenu)FindResource("TrayMenu");
        menu.DataContext = _vm;

        _tray = new TaskbarIcon
        {
            ToolTipText = "PricePin",
            IconSource = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/PricePin.ico")),
            ContextMenu = menu,
            DataContext = _vm,
            LeftClickCommand = _vm!.ToggleAllWidgetsCommand,
            NoLeftClickDelay = true,
        };
        _tray.SetBinding(TaskbarIcon.ToolTipTextProperty, new Binding(nameof(AppViewModel.TrayToolTip)));
        // false = don't put the process in Windows "efficiency mode"; prices must stay real-time.
        _tray.ForceCreate(false);
    }

    private void ShowCoinDetail(string symbol)
    {
        if (_exiting)
            return;
        if (!_detailWindows.TryGetValue(symbol, out var window))
        {
            window = new CoinDetailWindow(new CoinDetailViewModel(_vm!, _candles, symbol));
            _detailWindows[symbol] = window;
            window.Closed += (_, _) => _detailWindows.Remove(symbol);
            window.Show();
        }
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void ShowMarket()
    {
        if (_exiting)
            return;
        if (_marketWindow is null)
        {
            _marketWindow = new MarketWindow(new MarketViewModel(_vm!, _overview));
            _marketWindow.Closed += (_, _) => _marketWindow = null;
            _marketWindow.Show();
        }
        if (_marketWindow.WindowState == WindowState.Minimized)
            _marketWindow.WindowState = WindowState.Normal;
        _marketWindow.Activate();
    }

    /// <summary>The shortcut opens quick search, or closes it when it is already open.</summary>
    private void ToggleQuickSearch()
    {
        if (_exiting)
            return;
        if (_quickSearch is not null)
        {
            _quickSearch.Close();
            return;
        }
        _quickSearch = new QuickSearchWindow(new QuickSearchViewModel(_vm!, _catalog!, _logos!));
        _quickSearch.Closed += (_, _) => _quickSearch = null;
        _quickSearch.Show();
        _quickSearch.Activate();
    }

    private ImageSource? _logoIcon;

    private void UpdateTrayIcon()
    {
        if (_tray is null || _vm is null)
            return;
        try
        {
            _logoIcon ??= _tray.IconSource;
            _tray.IconSource = _vm.TrayIconText is { } text ? TrayIconRenderer.Render(text, _vm.TrayIconUp) : _logoIcon;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex); // a drawing hiccup must never take the app down
        }
    }

    private void ShowAlert(IReadOnlyList<Core.Alerts.AlertHit> hits)
    {
        if (_tray is null || _vm is null)
            return;
        var quote = _vm.Settings.QuoteCurrency;
        string Line(Core.Alerts.AlertHit h)
        {
            var now = CoinRowViewModel.FormatPrice(h.Price, quote);
            return h.ChangePercent is { } c
                ? Loc.F("Alerts.NotifyLineChange", now, (c >= 0 ? "▲ " : "▼ ") + Math.Abs(c).ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "%")
                : Loc.F("Alerts.NotifyLine", now);
        }

        var title = hits.Count == 1 ? AlertsViewModel.Describe(hits[0].Alert) : Loc.F("Alerts.NotifyMany", hits.Count);
        var body = hits.Count == 1
            ? Line(hits[0])
            : string.Join("\n", hits.Take(4).Select(h => AlertsViewModel.Describe(h.Alert)));
        _tray.ShowNotification(title, body, NotificationIcon.Info, sound: _vm.Alerts.AlertSound);
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null || _vm is null)
            return;
        _hotkeys.UnregisterAll();

        var failed = new List<string>();
        if (!string.IsNullOrWhiteSpace(_vm.HotkeyToggleWidgets) && !_hotkeys.Register(_vm.HotkeyToggleWidgets, _vm.ToggleAllWidgets))
            failed.Add(_vm.HotkeyToggleWidgets);
        if (!string.IsNullOrWhiteSpace(_vm.HotkeyToggleClickThrough) && !_hotkeys.Register(_vm.HotkeyToggleClickThrough, _vm.ToggleClickThroughAll))
            failed.Add(_vm.HotkeyToggleClickThrough);
        if (!string.IsNullOrWhiteSpace(_vm.HotkeyTogglePrivacy) && !_hotkeys.Register(_vm.HotkeyTogglePrivacy, _vm.Portfolio.TogglePrivacy))
            failed.Add(_vm.HotkeyTogglePrivacy);
        if (!string.IsNullOrWhiteSpace(_vm.HotkeyQuickSearch) && !_hotkeys.Register(_vm.HotkeyQuickSearch, ToggleQuickSearch))
            failed.Add(_vm.HotkeyQuickSearch);

        _vm.HotkeyWarning = failed.Count == 0
            ? null
            : Loc.F("Hotkey.Failed", string.Join(Loc.T("Hotkey.And"), failed));
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            if (_vm!.PendingSettingsPage is { } openPage && _settingsWindow.DataContext is SettingsViewModel open)
                open.Page = openPage;
            _vm.PendingSettingsPage = null;
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        var vm = new SettingsViewModel(_vm!, _settings!, _catalog!, _logos!, _theme!);
        if (_vm!.PendingSettingsPage is { } page)
            vm.Page = page;
        _vm.PendingSettingsPage = null;
        _settingsWindow = new SettingsWindow(vm);
        _settingsWindow.Closed += (_, _) =>
        {
            vm.Dispose();
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ListenForShowSignal()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + InstanceId + ".Show");
        var thread = new Thread(() =>
        {
            try
            {
                while (_showSignal.WaitOne())
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        foreach (var w in _vm?.Widgets ?? [])
                            w.IsWidgetVisible = true;
                    });
                }
            }
            catch (ObjectDisposedException)
            {
                // App is shutting down.
            }
        })
        {
            IsBackground = true,
            Name = "PricePin single-instance listener",
        };
        thread.Start();
    }

    /// <summary>Tidies up like a normal exit, then hands over to Velopack to install and restart.</summary>
    private void RestartForUpdate()
    {
        if (_exiting)
            return;
        _exiting = true;
        _settings?.SaveNow();
        _settingsWindow?.Close();
        foreach (var widget in _windows.Keys.ToList())
            CloseWindowFor(widget); // also gives back any reserved ticker-bar space
        _hotkeys?.Dispose();
        _tray?.Dispose();
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        try
        {
            _updates?.ApplyAndRestart(); // exits the process
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
        Shutdown();
    }

    private void ExitApp()
    {
        if (_exiting)
            return;
        _exiting = true;
        _settings?.SaveNow();
        _settingsWindow?.Close();
        foreach (var widget in _windows.Keys.ToList())
            CloseWindowFor(widget);
        Shutdown();
    }
}
