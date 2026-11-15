using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Threading;
using PricePin.Core.Market;
using PricePin.Core.Settings;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>
/// One widget (list card or ticker bar): its coins, look and window behaviour.
/// Every change is written to its <see cref="WidgetSettings"/> and saved.
/// </summary>
public sealed partial class WidgetViewModel : ObservableObject
{
    private readonly Dictionary<string, CoinRowViewModel> _rowsBySymbol = new(StringComparer.OrdinalIgnoreCase);

    public WidgetViewModel(AppViewModel app, WidgetSettings settings)
    {
        App = app;
        Settings = settings;

        _name = settings.Name;
        _layout = settings.Layout;
        _isWidgetVisible = settings.Visible;
        _topmost = settings.Topmost;
        _opacity = settings.Opacity;
        _clickThrough = settings.ClickThrough;
        _lockPosition = settings.LockPosition;
        _snapToEdges = settings.SnapToEdges;
        _size = settings.Size;
        _showNames = settings.ShowNames;
        _showHoldings = settings.ShowHoldings;
        _showSparklines = settings.ShowSparklines;
        _showColumnHeadings = settings.ShowColumnHeadings;
        _dock = settings.Dock;
        _reserveSpace = settings.ReserveSpace;
        _scroll = settings.Scroll;
        _scrollSpeed = settings.ScrollSpeed;
        _floatingWidth = settings.FloatingWidth;
        _changePeriod = settings.ChangePeriod;
        _showNews = settings.ShowNews;
        _showFees = settings.ShowFees;
        _showEvents = settings.ShowEvents;
        _headlineTimer = new DispatcherTimer(HeadlineEvery, DispatcherPriority.Background, (_, _) => NextHeadline(), Dispatcher.CurrentDispatcher);
        _headlineTimer.Stop();
    }

    private static readonly TimeSpan HeadlineEvery = TimeSpan.FromSeconds(12);
    private readonly DispatcherTimer _headlineTimer;
    private IReadOnlyList<HeadlineViewModel> _headlines = [];
    private int _headlineIndex;

    /// <summary>What a ticker bar scrolls: the coins, then fees and headlines when they are turned on.</summary>
    public ObservableCollection<object> TickerItems { get; } = [];

    [ObservableProperty] private bool _showNews;
    [ObservableProperty] private bool _showFees;
    [ObservableProperty] private bool _showEvents;

    /// <summary>The headline a list widget shows under its coins; changes every few seconds.</summary>
    [ObservableProperty] private HeadlineViewModel? _currentHeadline;

    partial void OnShowNewsChanged(bool value)
    {
        Save(s => s.ShowNews = value);
        App.Pulse.UpdateDemand();
        RefreshPulse();
    }

    partial void OnShowFeesChanged(bool value)
    {
        Save(s => s.ShowFees = value);
        App.Pulse.UpdateDemand();
        RebuildTickerItems();
    }

    partial void OnShowEventsChanged(bool value)
    {
        Save(s => s.ShowEvents = value);
        App.Pulse.Calendar.OnTick();
        RebuildTickerItems();
    }

    /// <summary>Picks up new headlines (called by the app when the news refreshes).</summary>
    public void RefreshPulse()
    {
        _headlines = ShowNews ? App.Pulse.HeadlinesFor(Settings.Coins) : [];
        if (_headlines.Count > 0 && !IsTickerBar)
        {
            _headlineIndex %= _headlines.Count;
            CurrentHeadline = _headlines[_headlineIndex];
            _headlineTimer.Start();
        }
        else
        {
            CurrentHeadline = null;
            _headlineTimer.Stop();
        }
        RebuildTickerItems();
    }

    private void NextHeadline()
    {
        if (_headlines.Count == 0)
            return;
        _headlineIndex = (_headlineIndex + 1) % _headlines.Count;
        CurrentHeadline = _headlines[_headlineIndex];
    }

    private void RebuildTickerItems()
    {
        var items = new List<object>(Coins);
        if (ShowFees)
            items.Add(App.Pulse.Fees);
        if (ShowEvents && App.Pulse.Calendar.Next.HasEvent)
            items.Add(App.Pulse.Calendar.Next);
        if (IsTickerBar)
            items.AddRange(_headlines.Take(5));
        if (items.SequenceEqual(TickerItems))
            return;
        TickerItems.Clear();
        foreach (var item in items)
            TickerItems.Add(item);
    }

    /// <summary>Stops the headline timer when the widget is removed.</summary>
    public void Detach() => _headlineTimer.Stop();

    /// <summary>Raised when the layout changes, so the app can swap the window type.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Raised when the ticker bar's docking needs re-applying.</summary>
    public event EventHandler? DockingChanged;

    public AppViewModel App { get; }
    public WidgetSettings Settings { get; }
    public ObservableCollection<CoinRowViewModel> Coins { get; } = [];

    public double OpacityMin => OpacityLimits.Min;
    public double OpacityMax => OpacityLimits.Max;
    public string OpacityText => AppViewModel.Percent(Opacity);

    [ObservableProperty] private string _name;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsTickerBar), nameof(LayoutLabel))] private WidgetLayout _layout;
    [ObservableProperty] private bool _isWidgetVisible;
    [ObservableProperty] private bool _topmost;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OpacityText))] private double _opacity;
    [ObservableProperty] private bool _clickThrough;
    [ObservableProperty] private bool _lockPosition;
    [ObservableProperty] private bool _snapToEdges;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Scale))] private WidgetSize _size;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CardWidth))] private bool _showNames;
    [ObservableProperty] private bool _showHoldings;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CardWidth))] private bool _showSparklines;
    [ObservableProperty] private bool _showColumnHeadings;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsFloating))] private BarDock _dock;
    [ObservableProperty] private bool _reserveSpace;
    [ObservableProperty] private bool _scroll;
    [ObservableProperty] private double _scrollSpeed;
    [ObservableProperty] private double _floatingWidth;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ChangeHeading))] private ChangePeriod _changePeriod;

    /// <summary>Column heading over the sparkline: "1H", "24H" or "7D".</summary>
    public string ChangeHeading => Loc.T(ChangePeriod switch
    {
        ChangePeriod.Hour => "Period.Hour",
        ChangePeriod.Week => "Period.Week",
        _ => "Period.Day",
    }).ToUpper(System.Globalization.CultureInfo.CurrentUICulture);

    [RelayCommand]
    private void SetChangePeriod(string? period)
    {
        if (Enum.TryParse<ChangePeriod>(period, out var p))
            ChangePeriod = p;
    }

    partial void OnChangePeriodChanged(ChangePeriod value)
    {
        Save(s => s.ChangePeriod = value);
        foreach (var row in Coins)
            row.SetPeriod(value);
        _ = App.LoadHistoryAsync(Coins.ToList(), value);
    }

    public bool IsTickerBar => Layout == WidgetLayout.TickerBar;
    public bool IsFloating => Dock == BarDock.Floating;
    public string LayoutLabel => Loc.T(IsTickerBar ? "Widgets.Bar" : "Widgets.List");

    /// <summary>Re-reads text after the UI language changes.</summary>
    public void RefreshText()
    {
        OnPropertyChanged(nameof(LayoutLabel));
        OnPropertyChanged(nameof(ChangeHeading));
        foreach (var c in Coins)
            c.RefreshText();
    }

    /// <summary>Zoom applied to the whole widget.</summary>
    public double Scale => Size switch
    {
        WidgetSize.Small => 0.86,
        WidgetSize.Large => 1.18,
        _ => 1.0,
    };

    /// <summary>List card width before scaling; narrower when columns are hidden.</summary>
    public double CardWidth => 320 - (ShowSparklines ? 0 : 64) - (ShowNames ? 0 : 36);

    partial void OnNameChanged(string value) => Save(s => s.Name = string.IsNullOrWhiteSpace(value) ? Loc.T("Widget.FallbackName") : value.Trim());

    partial void OnLayoutChanged(WidgetLayout value)
    {
        Save(s => s.Layout = value);
        RefreshPulse();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsWidgetVisibleChanged(bool value)
    {
        Save(s => s.Visible = value);
        App.RaiseWidgetStateChanged();
    }

    partial void OnTopmostChanged(bool value) => Save(s => s.Topmost = value);

    partial void OnOpacityChanged(double value)
    {
        var clamped = double.IsFinite(value) ? Math.Clamp(value, OpacityLimits.Min, OpacityLimits.Max) : OpacityLimits.Max;
        if (clamped != value)
        {
            Opacity = clamped;
            return;
        }
        Save(s => s.Opacity = value);
    }

    partial void OnClickThroughChanged(bool value)
    {
        Save(s => s.ClickThrough = value);
        App.RaiseWidgetStateChanged();
    }

    partial void OnLockPositionChanged(bool value) => Save(s => s.LockPosition = value);
    partial void OnSnapToEdgesChanged(bool value) => Save(s => s.SnapToEdges = value);
    partial void OnSizeChanged(WidgetSize value) => Save(s => s.Size = value);
    partial void OnShowNamesChanged(bool value) => Save(s => s.ShowNames = value);
    partial void OnShowHoldingsChanged(bool value) => Save(s => s.ShowHoldings = value);
    partial void OnShowSparklinesChanged(bool value) => Save(s => s.ShowSparklines = value);
    partial void OnShowColumnHeadingsChanged(bool value) => Save(s => s.ShowColumnHeadings = value);
    partial void OnScrollChanged(bool value) => Save(s => s.Scroll = value);
    partial void OnScrollSpeedChanged(double value) => Save(s => s.ScrollSpeed = Math.Clamp(value, 10, 200));

    partial void OnDockChanged(BarDock value)
    {
        Save(s => s.Dock = value);
        DockingChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnReserveSpaceChanged(bool value)
    {
        Save(s => s.ReserveSpace = value);
        DockingChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnFloatingWidthChanged(double value)
    {
        Save(s => s.FloatingWidth = Math.Clamp(value, 320, 4000));
        DockingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SavePosition(double left, double top) => Save(s =>
    {
        s.Left = left;
        s.Top = top;
    });

    [RelayCommand]
    private void ToggleVisibility() => IsWidgetVisible = !IsWidgetVisible;

    [RelayCommand]
    private void SwitchLayout() => Layout = IsTickerBar ? WidgetLayout.List : WidgetLayout.TickerBar;

    [RelayCommand]
    private void SetDock(string? dock)
    {
        if (Enum.TryParse<BarDock>(dock, out var d))
            Dock = d;
    }

    /// <summary>
    /// Rebuilds the rows after the coin list or currency changed. Rows for coins that
    /// stay keep their price and sparkline; new ones load their logo and history.
    /// </summary>
    public void ApplyCoinSettings(bool currencyChanged = false)
    {
        if (currencyChanged)
            _rowsBySymbol.Clear();

        var quote = App.Settings.QuoteCurrency;
        var added = new List<CoinRowViewModel>();
        var rows = new List<CoinRowViewModel>();
        foreach (var symbol in Settings.Coins)
        {
            if (!_rowsBySymbol.TryGetValue(symbol, out var row))
            {
                row = new CoinRowViewModel(symbol, quote, ChangePeriod) { Logo = App.Logos.GetLocal(symbol) };
                added.Add(row);
            }
            rows.Add(row);
        }

        _rowsBySymbol.Clear();
        Coins.Clear();
        foreach (var row in rows)
        {
            _rowsBySymbol[row.Key] = row;
            Coins.Add(row);
        }

        RefreshPulse();
        _ = App.LoadLogosAsync(added);
        _ = App.LoadHistoryAsync(added, ChangePeriod);
    }

    /// <summary>Called by the app with the latest merged updates.</summary>
    public void Apply(IEnumerable<Ticker> batch)
    {
        foreach (var t in batch)
        {
            if (_rowsBySymbol.TryGetValue(t.Base, out var row))
                row.Apply(t);
        }
    }

    private void Save(Action<WidgetSettings> apply)
    {
        apply(Settings);
        App.Save();
    }
}
