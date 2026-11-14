using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Models;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>
/// State behind the widget, its right-click menu and the tray menu. Every change is
/// written back to <see cref="AppSettings"/> and saved.
/// </summary>
public sealed partial class WidgetViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public WidgetViewModel(SettingsService settings)
    {
        _settings = settings;
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

        Coins = new ObservableCollection<CoinRowViewModel>(DemoData.Create(s.Coins));
    }

    /// <summary>Raised when the user picks Exit.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Raised when the user asks to show or hide the widget.</summary>
    public event EventHandler? ToggleVisibilityRequested;

    public ObservableCollection<CoinRowViewModel> Coins { get; }

    /// <summary>Phase 1 shows sample prices; live feeds replace this in phase 2.</summary>
    public string StatusText => "Sample prices · live data coming next";

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

    [RelayCommand]
    private void SetOpacityPercent(string? percent)
    {
        if (double.TryParse(percent, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            Opacity = p / 100.0;
    }

    [RelayCommand]
    private void ToggleVisibility() => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void Save(Action<AppSettings> apply)
    {
        apply(_settings.Current);
        _settings.ScheduleSave();
    }

    /// <summary>Placeholder prices so the layout can be judged before live data exists.</summary>
    private static class DemoData
    {
        private static readonly Dictionary<string, (string Name, string Color, decimal Price, decimal Change)> Known = new()
        {
            ["BTC"] = ("Bitcoin", "#F7931A", 97_412.35m, 2.14m),
            ["ETH"] = ("Ethereum", "#627EEA", 3_481.07m, -0.82m),
            ["SOL"] = ("Solana", "#14F195", 214.66m, 5.37m),
            ["BNB"] = ("BNB", "#F3BA2F", 662.40m, 0.41m),
            ["XRP"] = ("XRP", "#8FA3B8", 1.1243m, -1.95m),
            ["DOGE"] = ("Dogecoin", "#C2A633", 0.38712m, 3.08m),
            ["ADA"] = ("Cardano", "#3468D1", 0.8125m, -0.33m),
        };

        public static IEnumerable<CoinRowViewModel> Create(IEnumerable<string> symbols)
        {
            foreach (var symbol in symbols)
            {
                var (name, color, price, change) = Known.TryGetValue(symbol, out var k)
                    ? k
                    : (symbol, "#64748B", 0m, 0m);
                yield return new CoinRowViewModel(symbol, name, (Color)ColorConverter.ConvertFromString(color))
                {
                    Price = price,
                    ChangePercent24h = change,
                };
            }
        }
    }
}
