using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Coins;
using PricePin.Core.Market;
using PricePin.Core.Portfolio;
using PricePin.Core.Settings;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>One holding on the Portfolio page. Amount and cost are edited in place.</summary>
public sealed partial class HoldingItemViewModel : ObservableObject
{
    private readonly PortfolioViewModel _owner;

    public HoldingItemViewModel(Holding holding, PortfolioViewModel owner, ImageSource? logo)
    {
        Holding = holding;
        _owner = owner;
        _logo = logo;
        _amountText = PortfolioViewModel.FormatAmount(holding.Amount);
        _costText = holding.AverageCost is { } c ? CoinRowViewModel.FormatNumber(c).Replace(",", "") : "";
    }

    public Holding Holding { get; }

    /// <summary>The coin key ("BTC" or "dex:chain:address").</summary>
    public string Key => Holding.Symbol;
    public string Symbol => CoinCatalog.DisplaySymbol(Key);
    public string Initial => Symbol.Length > 0 ? Symbol[..1] : "?";

    [ObservableProperty]
    private ImageSource? _logo;

    [ObservableProperty]
    private string _amountText;

    [ObservableProperty]
    private string _costText;

    [ObservableProperty]
    private string _valueText = "—";

    [ObservableProperty]
    private string _profitText = "";

    [ObservableProperty]
    private bool _isProfit = true;

    [ObservableProperty]
    private bool _hasError;

    partial void OnAmountTextChanged(string value)
    {
        HasError = !PriceText.TryParse(value, CultureInfo.CurrentCulture, out var amount) || amount <= 0;
        if (!HasError)
            _owner.SetAmount(this, amount);
    }

    partial void OnCostTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            HasError = false;
            _owner.SetCost(this, null);
            return;
        }
        HasError = !PriceText.TryParse(value, CultureInfo.CurrentCulture, out var cost) || cost < 0;
        if (!HasError)
            _owner.SetCost(this, cost);
    }

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    internal void SetValues(string value, string profit, bool isProfit)
    {
        ValueText = value;
        ProfitText = profit;
        IsProfit = isProfit;
    }
}

/// <summary>
/// The portfolio: holdings, their live value and profit/loss, and privacy mode, which hides
/// every amount and value (in widgets, Settings and the tray tooltip) for screen sharing.
/// </summary>
public sealed partial class PortfolioViewModel : ObservableObject
{
    public const string Mask = "•••••";

    private readonly AppViewModel _app;
    private string _lastTotals = "";

    public PortfolioViewModel(AppViewModel app)
    {
        _app = app;
        _privacyMode = app.Settings.PrivacyMode;
        Items = new(app.Settings.Holdings.Select(h => new HoldingItemViewModel(h, this, app.Logos.GetLocal(h.Symbol))));
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasHoldings));
        _newSymbol = "";
    }

    public ObservableCollection<HoldingItemViewModel> Items { get; }
    public bool HasHoldings => Items.Count > 0;
    public string Quote => _app.Settings.QuoteCurrency;

    public string CostHint => Loc.F("Portfolio.Optional", Quote);

    public string PrivacyHint =>
        Loc.F("Portfolio.PrivacyHint", string.IsNullOrWhiteSpace(_app.HotkeyTogglePrivacy) ? "—" : _app.HotkeyTogglePrivacy);

    internal void RaiseHintsChanged()
    {
        OnPropertyChanged(nameof(CostHint));
        OnPropertyChanged(nameof(PrivacyHint));
    }

    /// <summary>Coins offered in the add form: every coin shown in a widget.</summary>
    public IReadOnlyList<string> SymbolChoices =>
        _app.Widgets.SelectMany(w => w.Settings.Coins).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Held coins; the feed subscribes to them even if no widget shows them.</summary>
    public IEnumerable<string> WatchedSymbols => _app.Settings.Holdings.Select(h => h.Symbol);

    [ObservableProperty]
    private bool _privacyMode;

    partial void OnPrivacyModeChanged(bool value)
    {
        _app.Settings.PrivacyMode = value;
        _app.Save();
        Recalculate(force: true);
    }

    [RelayCommand]
    public void TogglePrivacy() => PrivacyMode = !PrivacyMode;

    // ---- Totals ----------------------------------------------------------------------------

    [ObservableProperty]
    private string _totalValueText = "—";

    [ObservableProperty]
    private string _totalChangeText = "";

    [ObservableProperty]
    private bool _totalUp = true;

    [ObservableProperty]
    private string _totalProfitText = "";

    [ObservableProperty]
    private bool _totalProfitUp = true;

    /// <summary>"Portfolio $12,345.67 ▲ 2.10%" for the tray tooltip; null without holdings or in privacy mode.</summary>
    public string? Summary { get; private set; }

    // ---- Add form --------------------------------------------------------------------------

    /// <summary>Coin key of the form; set from the chips or the coin chart.</summary>
    [ObservableProperty]
    private string _newSymbol;

    /// <summary>What the coin box shows: the symbol, even for DEX tokens.</summary>
    [ObservableProperty]
    private string _newSymbolText = "";

    private bool _syncingSymbolText;

    partial void OnNewSymbolChanged(string value)
    {
        _syncingSymbolText = true;
        NewSymbolText = value.Length == 0 ? "" : CoinCatalog.DisplaySymbol(value);
        _syncingSymbolText = false;
    }

    partial void OnNewSymbolTextChanged(string value)
    {
        if (!_syncingSymbolText)
            NewSymbol = CoinCatalog.ResolveTyped(value, SymbolChoices);
    }

    [ObservableProperty]
    private string _newAmount = "";

    [ObservableProperty]
    private string _newCost = "";

    [ObservableProperty]
    private string? _newError;

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
        if (!PriceText.TryParse(NewAmount, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            NewError = Loc.T("Portfolio.BadAmount");
            return;
        }
        decimal? cost = null;
        if (!string.IsNullOrWhiteSpace(NewCost))
        {
            if (!PriceText.TryParse(NewCost, CultureInfo.CurrentCulture, out var c) || c < 0)
            {
                NewError = Loc.T("Alerts.BadValue");
                return;
            }
            cost = c;
        }

        var existing = Items.FirstOrDefault(i => string.Equals(i.Key, symbol, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            // Buying more of a coin: amounts add up, cost becomes the weighted average.
            var h = existing.Holding;
            var total = h.Amount + amount;
            h.AverageCost = h.AverageCost is { } oldCost && cost is { } newCost && h.CostQuote == Quote
                ? (oldCost * h.Amount + newCost * amount) / total
                : h.AverageCost ?? cost;
            h.Amount = total;
            Items.Remove(existing);
        }
        else if (_app.Settings.Holdings.Count >= AppSettings.MaxHoldings)
        {
            NewError = Loc.F("Portfolio.TooMany", AppSettings.MaxHoldings);
            return;
        }
        else
            _app.Settings.Holdings.Add(new Holding { Symbol = symbol, Amount = amount, AverageCost = cost, CostQuote = Quote });

        var holding = _app.Settings.Holdings.First(h => string.Equals(h.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        var item = new HoldingItemViewModel(holding, this, _app.Logos.GetLocal(symbol));
        Items.Insert(0, item);
        _ = LoadLogoAsync(item);

        NewError = null;
        NewAmount = "";
        NewCost = "";
        _app.Save();
        _app.EnsureSubscribed(symbol);
        Recalculate(force: true);
    }

    // ---- Called by the rows -------------------------------------------------------------------

    internal void SetAmount(HoldingItemViewModel item, decimal amount)
    {
        item.Holding.Amount = amount;
        _app.Save();
        Recalculate(force: true);
    }

    internal void SetCost(HoldingItemViewModel item, decimal? cost)
    {
        item.Holding.AverageCost = cost;
        item.Holding.CostQuote = Quote;
        _app.Save();
        Recalculate(force: true);
    }

    internal void Remove(HoldingItemViewModel item)
    {
        Items.Remove(item);
        _app.Settings.Holdings.Remove(item.Holding);
        _app.Save();
        Recalculate(force: true);
    }

    // ---- Live values ---------------------------------------------------------------------------

    /// <summary>Recomputes values from the latest prices. Cheap; called after each price batch.</summary>
    public void Recalculate(bool force = false)
    {
        var quote = Quote;
        var lines = new List<PortfolioLine>();
        foreach (var h in _app.Settings.Holdings)
        {
            decimal? price = null, change = null;
            if (_app.Alerts.TryGetLatest(h.Symbol, out var p, out var c))
                (price, change) = (p, c);
            var cost = h.CostQuote == quote ? h.AverageCost : null;
            lines.Add(new PortfolioLine(h.Symbol, h.Amount, price, change, cost));
        }
        var totals = PortfolioMath.Totals(lines);

        // Rows in the widgets.
        var bySymbol = lines.ToDictionary(l => l.Symbol, StringComparer.OrdinalIgnoreCase);
        foreach (var row in _app.Widgets.SelectMany(w => w.Coins))
        {
            if (bySymbol.TryGetValue(row.Key, out var line))
                row.SetHolding(PrivacyMode
                    ? Mask
                    : $"{FormatAmount(line.Amount)} · {(line.Value is { } v ? CoinRowViewModel.FormatPrice(v, quote) : "—")}");
            else
                row.SetHolding(null);
        }

        // Rows on the Portfolio page.
        foreach (var item in Items)
        {
            var line = bySymbol.GetValueOrDefault(item.Key);
            if (line is null)
                continue;
            var value = line.Value is { } v ? CoinRowViewModel.FormatPrice(v, quote) : "—";
            var profit = line.ProfitLoss is { } pl ? Signed(pl, quote) + Percent(line.ProfitLossPercent) : "";
            item.SetValues(PrivacyMode ? Mask : value, PrivacyMode && profit.Length > 0 ? Mask : profit, (line.ProfitLoss ?? 0) >= 0);
        }

        // Totals: only touch bindings when the text actually changes.
        var totalValue = PrivacyMode ? Mask : CoinRowViewModel.FormatPrice(totals.Value, quote);
        var totalChange = totals.Change24h is { } ch ? (PrivacyMode ? "" : Signed(ch, quote)) + Percent(totals.ChangePercent24h) : "";
        var totalProfit = totals.ProfitLoss is { } tp ? (PrivacyMode ? Mask : Signed(tp, quote) + Percent(totals.ProfitLossPercent)) : "";
        var key = totalValue + totalChange + totalProfit;
        if (!force && key == _lastTotals)
            return;
        _lastTotals = key;
        TotalValueText = totalValue;
        TotalChangeText = totalChange;
        TotalUp = (totals.Change24h ?? 0) >= 0;
        TotalProfitText = totalProfit;
        TotalProfitUp = (totals.ProfitLoss ?? 0) >= 0;
        Summary = !HasHoldings || PrivacyMode ? null : Loc.F("Portfolio.Summary", totalValue, Percent(totals.ChangePercent24h).Trim());
    }

    public void OnQuoteChanged()
    {
        OnPropertyChanged(nameof(Quote));
        RaiseHintsChanged();
        Recalculate(force: true);
    }

    public void RefreshText()
    {
        RaiseHintsChanged();
        Recalculate(force: true);
    }

    internal static string FormatAmount(decimal amount) =>
        amount.ToString(Math.Abs(amount) >= 1000 ? "#,0.##" : "0.########", CultureInfo.InvariantCulture);

    private static string Signed(decimal value, string quote) =>
        (value >= 0 ? "+" : "−") + CoinRowViewModel.FormatPrice(Math.Abs(value), quote);

    private static string Percent(decimal? pct) =>
        pct is { } p ? $" ({(p >= 0 ? "+" : "−")}{Math.Abs(p).ToString("0.00", CultureInfo.InvariantCulture)}%)" : "";

    private async Task LoadLogoAsync(HoldingItemViewModel item)
    {
        if (item.Logo is not null)
            return;
        try
        {
            item.Logo = await _app.Logos.GetAsync(item.Key, CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    internal async Task LoadLogosAsync()
    {
        foreach (var item in Items.ToList())
            await LoadLogoAsync(item);
    }
}
