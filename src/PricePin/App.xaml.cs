using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using PricePin.Core.Coins;
using PricePin.Core.History;
using PricePin.Core.Icons;
using PricePin.Core.Market;
using PricePin.Services;
using PricePin.ViewModels;
using PricePin.Views;

namespace PricePin;

public partial class App : Application
{
    // Unique per user session; a second launch signals the first one to show itself and quits.
    private const string InstanceId = "PricePin.SingleInstance.5B0E4D8C";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private SettingsService? _settings;
    private MarketDataService? _market;
    private ThemeService? _theme;
    private LogoProvider? _logos;
    private CoinCatalogService? _catalog;
    private SettingsWindow? _settingsWindow;
    private WidgetViewModel? _vm;
    private WidgetWindow? _widget;
    private TaskbarIcon? _tray;
    private bool _clickThroughHintShown;

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

        _market = new MarketDataService();
        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PricePin");
        _logos = new LogoProvider(new CoinIconService(Path.Combine(localData, "icons")));
        _catalog = new CoinCatalogService(Path.Combine(localData, "catalog"));
        _vm = new WidgetViewModel(_settings, _market, new PriceHistoryService(), _logos);
        _vm.ExitRequested += (_, _) => ExitApp();
        _vm.ToggleVisibilityRequested += (_, _) => SetWidgetVisible(!_vm.IsWidgetVisible);
        _vm.SettingsRequested += (_, _) => ShowSettings();
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        CreateTrayIcon();

        _widget = new WidgetWindow(_vm, _settings.Current.Left, _settings.Current.Top);
        _widget.Show();
        _vm.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose();
        _theme?.Dispose();
        // Close sockets politely, but never let shutdown hang on the network.
        _market?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _settings?.SaveNow();
        _tray?.Dispose();
        _showSignal?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        var menu = (ContextMenu)FindResource("AppMenu");
        menu.DataContext = _vm;

        _tray = new TaskbarIcon
        {
            ToolTipText = "PricePin",
            IconSource = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/PricePin.ico")),
            ContextMenu = menu,
            DataContext = _vm,
            LeftClickCommand = _vm!.ToggleVisibilityCommand,
            NoLeftClickDelay = true,
        };
        _tray.SetBinding(TaskbarIcon.ToolTipTextProperty, new Binding(nameof(WidgetViewModel.TrayToolTip)));
        // false = don't put the process in Windows "efficiency mode"; prices must stay real-time.
        _tray.ForceCreate(false);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        var vm = new SettingsViewModel(_vm!, _settings!, _catalog!, _logos!, _theme!);
        _settingsWindow = new SettingsWindow(vm);
        _settingsWindow.Closed += (_, _) =>
        {
            vm.Dispose();
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void SetWidgetVisible(bool visible)
    {
        if (_widget is null || _vm is null)
            return;

        if (visible)
        {
            _widget.Show();
            _widget.Activate();
        }
        else
        {
            _widget.Hide();
        }
        _vm.IsWidgetVisible = visible;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Click-through makes the widget ignore the mouse, so point people to the tray icon once.
        if (e.PropertyName == nameof(WidgetViewModel.ClickThrough) && _vm!.ClickThrough && !_clickThroughHintShown)
        {
            _clickThroughHintShown = true;
            _tray?.ShowNotification(
                "Click-through is on",
                "The mouse now passes through the widget. Right-click the PricePin tray icon to turn it off.",
                NotificationIcon.Info);
        }
    }

    private void ListenForShowSignal()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + InstanceId + ".Show");
        var thread = new Thread(() =>
        {
            try
            {
                while (_showSignal.WaitOne())
                    Dispatcher.BeginInvoke(() => SetWidgetVisible(true));
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

    private void ExitApp()
    {
        _settings?.SaveNow();
        _settingsWindow?.Close();
        _widget?.CloseForExit();
        Shutdown();
    }
}
