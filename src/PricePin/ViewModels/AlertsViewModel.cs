using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Alerts;
using PricePin.Core.Coins;
using PricePin.Core.Market;
using PricePin.Core.Settings;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>One alert in the Alerts page list.</summary>
public sealed partial class AlertItemViewModel : ObservableObject
{
    private readonly AlertsViewModel _owner;
    private bool _syncing;

    public AlertItemViewModel(PriceAlert alert, AlertsViewModel owner, ImageSource? logo)
    {
        Alert = alert;
        _owner = owner;
        _enabled = alert.Enabled;
        _logo = logo;
    }

    public PriceAlert Alert { get; }

    /// <summary>The coin key ("BTC" or "dex:chain:address").</summary>
    public string Key => Alert.Symbol;
    public string Symbol => CoinCatalog.DisplaySymbol(Key);
    public string Initial => Symbol.Length > 0 ? Symbol[..1] : "?";

    [ObservableProperty]
    private ImageSource? _logo;

    [ObservableProperty]
    private bool _enabled;

    public string Description => AlertsViewModel.Describe(Alert);

    /// <summary>"Repeats · Last alert 14:02" or "Paused: prices are showing in EUR".</summary>
    public string Details
    {
        get
        {
            if (!string.Equals(Alert.Quote, _owner.Quote, StringComparison.OrdinalIgnoreCase))
                return Loc.F("Alerts.Paused", _owner.Quote);
            var kind = Loc.T(Alert.Repeat ? "Alerts.Repeats" : "Alerts.Once");
            return Alert.LastTriggered is { } at
                ? kind + " · " + Loc.F("Alerts.LastFired", at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : kind;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_syncing)
            _owner.SetEnabled(this, value);
    }

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    /// <summary>Re-reads the alert after it fired or the language changed.</summary>
    public void Refresh()
    {
        if (Enabled != Alert.Enabled)
        {
            _syncing = true; // the alert changed itself (it fired); don't save it back
            Enabled = Alert.Enabled;
            _syncing = false;
        }
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Details));
    }
}

/// <summary>
/// Price alerts: the list, the "new alert" form, and checking every price update against them.
/// Alerts are checked against the merged prices the widgets get (about ten times a second).
/// </summary>
public sealed partial class AlertsViewModel : ObservableObject
{
    private readonly AppViewModel _app;
    private readonly AlertEngine _engine = new();
    private readonly Dictionary<string, decimal> _open24h = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (decimal Price, decimal? Change)> _latest = new(StringComparer.OrdinalIgnoreCase);
    private bool _prefilling;
    private bool _valueEdited;

    public AlertsViewModel(AppViewModel app)
    {
        _app = app;
        _alertSound = app.Settings.AlertSound;
        Items = new(app.Settings.Alerts.Select(a => new AlertItemViewModel(a, this, app.Logos.GetLocal(a.Symbol))));
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAlerts));
        _newSymbol = app.Widgets.SelectMany(w => w.Settings.Coins).FirstOrDefault() ?? "BTC";
        _newSymbolText = CoinCatalog.DisplaySymbol(_newSymbol);
    }

    /// <summary>Raised on the UI thread with every alert that fired in one price update.</summary>
    public event EventHandler<IReadOnlyList<AlertHit>>? Triggered;

    public ObservableCollection<AlertItemViewModel> Items { get; }
    public bool HasAlerts => Items.Count > 0;
    public string Quote => _app.Settings.QuoteCurrency;

    /// <summary>Coins offered in the form: every coin shown in a widget.</summary>
    public IReadOnlyList<string> SymbolChoices =>
        _app.Widgets.SelectMany(w => w.Settings.Coins).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Symbols with an enabled alert in the current currency; the feed subscribes to them too.</summary>
    public IEnumerable<string> WatchedSymbols =>
        _app.Settings.Alerts.Where(a => a.Enabled && string.Equals(a.Quote, Quote, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Symbol);

    [ObservableProperty]
    private bool _alertSound;

    partial void OnAlertSoundChanged(bool value)
    {
        _app.Settings.AlertSound = value;
        _app.Save();
    }

    // ---- New alert form ------------------------------------------------------------------

    /// <summary>Coin key of the form; set from the chips or the coin chart.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPriceText))]
    private string _newSymbol;

    /// <summary>What the coin box shows: the symbol, even for DEX tokens.</summary>
    [ObservableProperty]
    private string _newSymbolText = "";

    private bool _syncingSymbolText;

    partial void OnNewSymbolTextChanged(string value)
    {
        if (!_syncingSymbolText)
            NewSymbol = CoinCatalog.ResolveTyped(value, SymbolChoices);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPercent), nameof(ValueSuffix))]
    private AlertCondition _newCondition = AlertCondition.Above;

    [ObservableProperty]
    private string _newValue = "";

    [ObservableProperty]
    private bool _newRepeat;

    [ObservableProperty]
    private string? _newError;

    public bool IsPercent => NewCondition is AlertCondition.RisesBy or AlertCondition.FallsBy;

    /// <summary>Shown after the value box: "USD" or "%".</summary>
    public string ValueSuffix => IsPercent ? "%" : Quote;

    public string CurrentPriceText =>
        TryGetLatest(NewSymbol, out var price, out _)
            ? Loc.F("Alerts.Now", CoinRowViewModel.FormatPrice(price, Quote))
            : "";

    partial void OnNewSymbolChanged(string value)
    {
        _syncingSymbolText = true;
        NewSymbolText = value.Length == 0 ? "" : CoinCatalog.DisplaySymbol(value);
        _syncingSymbolText = false;
        NewError = null;
        _valueEdited = false;
        PrefillValue();
    }

    partial void OnNewConditionChanged(AlertCondition value)
    {
        NewError = null;
        _valueEdited = false;
        PrefillValue();
    }

    partial void OnNewValueChanged(string value)
    {
        if (!_prefilling)
            _valueEdited = true;
    }

    [RelayCommand]
    private void PickSymbol(string symbol) => NewSymbol = symbol;

    [RelayCommand]
    private void Add()
    {
        var symbol = CoinCatalog.ResolveTyped(NewSymbol, SymbolChoices);
        if (!CoinCatalog.IsValidKey(symbol))
        {
            NewError = Loc.T("Alerts.BadSymbol");
            return;
        }
        if (!PriceText.TryParse(NewValue, CultureInfo.CurrentCulture, out var value) || value <= 0)
        {
            NewError = Loc.T("Alerts.BadValue");
            return;
        }
        if (_app.Settings.Alerts.Count >= AppSettings.MaxAlerts)
        {
            NewError = Loc.F("Alerts.TooMany", AppSettings.MaxAlerts);
            return;
        }

        var alert = new PriceAlert
        {
            Symbol = symbol,
            Quote = Quote,
            Condition = NewCondition,
            Value = value,
            Repeat = NewRepeat,
        };
        _app.Settings.Alerts.Add(alert);
        Items.Insert(0, new AlertItemViewModel(alert, this, _app.Logos.GetLocal(symbol)));
        _ = LoadLogoAsync(Items[0]);
        NewError = null;
        _valueEdited = false;
        _app.Save();
        _app.EnsureSubscribed(symbol); // the coin may not be in any widget

        // Fire straight away if it's already true, e.g. "BTC below 100k" while BTC is at 95k.
        if (TryGetLatest(symbol, out var price, out var change))
            Check(symbol, price, change);
    }

    // ---- Called by the list rows ---------------------------------------------------------

    internal void SetEnabled(AlertItemViewModel item, bool enabled)
    {
        item.Alert.Enabled = enabled;
        if (enabled)
            _engine.Rearm(item.Alert.Id);
        item.Refresh();
        _app.Save();
        if (enabled)
            _app.EnsureSubscribed(item.Key);
    }

    internal void Remove(AlertItemViewModel item)
    {
        Items.Remove(item);
        _app.Settings.Alerts.Remove(item.Alert);
        _app.Save();
    }

    // ---- Live prices ---------------------------------------------------------------------

    /// <summary>Latest price and 24h change for any coin the feed delivers.</summary>
    public bool TryGetLatest(string? symbol, out decimal price, out decimal? change)
    {
        if (symbol is not null && _latest.TryGetValue(symbol, out var hit))
        {
            (price, change) = hit;
            return true;
        }
        (price, change) = (0, null);
        return false;
    }

    /// <summary>Takes one merged batch from the feed and fires any alerts it sets off.</summary>
    public void OnPrices(IEnumerable<Ticker> batch)
    {
        var hits = new List<AlertHit>();
        foreach (var t in batch)
        {
            if (t.Open24h is { } open && open > 0)
                _open24h[t.Base] = open;
            decimal? change = _open24h.TryGetValue(t.Base, out var o)
                ? Math.Round((t.Last - o) / o * 100m, 2)
                : t.ChangePercent24h ?? (_latest.TryGetValue(t.Base, out var prev) ? prev.Change : null);
            _latest[t.Base] = (t.Last, change);
            hits.AddRange(_engine.Evaluate(_app.Settings.Alerts, Quote, t.Base, t.Last, change, DateTimeOffset.Now));
        }
        OnPropertyChanged(nameof(CurrentPriceText));
        if (!_valueEdited && NewValue.Length == 0)
            PrefillValue();
        Report(hits);
    }

    /// <summary>Prices restart from scratch when the currency changes.</summary>
    public void OnQuoteChanged()
    {
        _open24h.Clear();
        _latest.Clear();
        OnPropertyChanged(nameof(ValueSuffix));
        OnPropertyChanged(nameof(CurrentPriceText));
        foreach (var item in Items)
            item.Refresh();
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(CurrentPriceText));
        foreach (var item in Items)
            item.Refresh();
    }

    /// <summary>"BTC above $70,000.00", "ETH up 5% in 24h".</summary>
    public static string Describe(PriceAlert a)
    {
        var value = a.IsPercent
            ? a.Value.ToString("0.##", CultureInfo.CurrentCulture)
            : CoinRowViewModel.FormatPrice(a.Value, a.Quote);
        return Loc.F(a.Condition switch
        {
            AlertCondition.Above => "Alerts.DescAbove",
            AlertCondition.Below => "Alerts.DescBelow",
            AlertCondition.RisesBy => "Alerts.DescRises",
            _ => "Alerts.DescFalls",
        }, CoinCatalog.DisplaySymbol(a.Symbol), value);
    }

    private void Check(string symbol, decimal price, decimal? change) =>
        Report(_engine.Evaluate(_app.Settings.Alerts, Quote, symbol, price, change, DateTimeOffset.Now));

    private void Report(List<AlertHit> hits)
    {
        if (hits.Count == 0)
            return;
        foreach (var item in Items.Where(i => hits.Any(h => h.Alert == i.Alert)))
            item.Refresh();
        _app.Save();
        Triggered?.Invoke(this, hits);
    }

    private void PrefillValue()
    {
        _prefilling = true;
        if (IsPercent)
            NewValue = "5";
        else if (TryGetLatest(NewSymbol, out var price, out _))
            NewValue = CoinRowViewModel.FormatNumber(price).Replace(",", "");
        else
            NewValue = "";
        _prefilling = false;
    }

    private async Task LoadLogoAsync(AlertItemViewModel item)
    {
        if (item.Logo is not null)
            return;
        try
        {
            item.Logo = await _app.Logos.GetAsync(item.Key, CancellationToken.None);
        }
        catch (Exception)
        {
            // Badge with the initial stays.
        }
    }

    internal async Task LoadLogosAsync()
    {
        foreach (var item in Items.ToList())
            await LoadLogoAsync(item);
    }
}
