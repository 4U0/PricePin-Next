using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Coins;
using PricePin.Core.Dex;
using PricePin.Core.Market;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>
/// Quick search (Ctrl+Alt+K): find any coin, or any DEX token by name or contract address,
/// see its live price, convert amounts, and open its chart or add it to a widget, alert or
/// the portfolio without going through Settings.
/// </summary>
public sealed partial class QuickSearchViewModel : ObservableObject, IDisposable
{
    private readonly AppViewModel _app;
    private readonly CoinCatalogService _catalog;
    private readonly LogoProvider _logos;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<CatalogCoin> _coins = [];
    private IReadOnlyList<DexPair> _dexResults = [];
    private CancellationTokenSource? _dexSearch;
    private CancellationTokenSource? _selection;
    private readonly System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
    private MarketDataService? _feed;
    private DexPriceService? _dexFeed;
    private (string Key, decimal Price, decimal? Change)? _own;
    private bool _converting;

    public QuickSearchViewModel(AppViewModel app, CoinCatalogService catalog, LogoProvider logos)
    {
        _app = app;
        _catalog = catalog;
        _logos = logos;
        Quote = app.Settings.QuoteCurrency;
        app.PricesUpdated += OnPricesUpdated;
        Loc.Changed += OnLanguageChanged;
        _ = LoadCatalogAsync();
    }

    /// <summary>Asks the window to close, e.g. after opening a chart.</summary>
    public event EventHandler? CloseRequested;

    public string Quote { get; }

    public ObservableCollection<CatalogItemViewModel> Results { get; } = [];

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private CatalogItemViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _priceText = "—";

    [ObservableProperty]
    private string _changeText = "";

    [ObservableProperty]
    private bool _isUp = true;

    [ObservableProperty]
    private IReadOnlyList<double> _sparkline = [];

    /// <summary>Converter: amount of the coin.</summary>
    [ObservableProperty]
    private string _amountText = "1";

    /// <summary>Converter: value in the display currency.</summary>
    [ObservableProperty]
    private string _valueText = "";

    /// <summary>"Added to Main" after the add button, so people see it worked.</summary>
    [ObservableProperty]
    private string _feedback = "";

    partial void OnQueryChanged(string value)
    {
        _dexResults = [];
        Refresh();
        _ = SearchDexAsync(value);
    }

    partial void OnSelectedChanged(CatalogItemViewModel? value)
    {
        Feedback = "";
        PriceText = "—";
        ChangeText = "";
        Sparkline = [];
        ValueText = "";
        _own = null;
        StopOwnFeed();
        _selection?.Cancel();
        if (value is null)
            return;

        // A DEX token must be known (pool, chain) before it can be priced or charted.
        if (value.Pair is { } pair && CoinCatalog.GetToken(value.Key) is null)
            CoinCatalog.Register(pair.ToToken());
        UpdatePrice();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _selection = cts;
        _ = StartAfterPauseAsync(value.Key, cts.Token);
    }

    /// <summary>
    /// Waits until the arrow keys stop moving, then streams the coin on a connection of its own
    /// (so the widgets' connection isn't restarted) unless the widgets already stream it.
    /// </summary>
    private async Task StartAfterPauseAsync(string key, CancellationToken ct)
    {
        try
        {
            await Task.Delay(250, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        _ = LoadSparklineAsync(key);
        if (_app.IsSubscribed(key))
            return;
        if (CoinKey.IsDex(key))
        {
            if (CoinCatalog.GetToken(key) is { } token)
            {
                _dexFeed = new DexPriceService();
                _dexFeed.TickReceived += OnOwnTick;
                _dexFeed.Start([token], Quote);
            }
        }
        else
        {
            _feed = new MarketDataService();
            _feed.TickReceived += OnOwnTick;
            _feed.Start([new MarketSymbol(key, Quote)], _app.Settings.DataSource);
        }
    }

    private void OnOwnTick(Ticker t) => _dispatcher.BeginInvoke(() =>
    {
        if (Selected?.Key is not { } key || !string.Equals(key, t.Base, StringComparison.OrdinalIgnoreCase))
            return;
        var change = t.Open24h is > 0 and var open ? Math.Round((t.Last - open) / open * 100m, 2) : t.ChangePercent24h ?? _own?.Change;
        _own = (key, t.Last, change);
        UpdatePrice();
    });

    private void StopOwnFeed()
    {
        if (_feed is { } feed)
        {
            feed.TickReceived -= OnOwnTick;
            _ = feed.DisposeAsync();
            _feed = null;
        }
        if (_dexFeed is { } dex)
        {
            dex.TickReceived -= OnOwnTick;
            _ = dex.DisposeAsync();
            _dexFeed = null;
        }
    }

    partial void OnAmountTextChanged(string value)
    {
        if (_converting)
            return;
        RecalculateValue();
    }

    partial void OnValueTextChanged(string value)
    {
        if (_converting || Selected is null || !TryGetPrice(out var price) || price <= 0)
            return;
        if (!PriceText_TryParse(value, out var v))
            return;
        _converting = true;
        AmountText = PortfolioViewModel.FormatAmount(v / price);
        _converting = false;
    }

    // ---- Commands ----------------------------------------------------------------------

    [RelayCommand]
    private void Pick(CatalogItemViewModel? item)
    {
        if (item is not null)
            Selected = item;
    }

    [RelayCommand]
    private void OpenChart()
    {
        if (Selected is not { } s)
            return;
        SaveTokenIfDex(s);
        _app.OpenCoinDetail(s.Key);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds the coin to the first list widget (or the first widget of any kind).</summary>
    [RelayCommand]
    private void AddToWidget()
    {
        if (Selected is not { } s)
            return;
        var widget = _app.Widgets.FirstOrDefault(w => !w.IsTickerBar) ?? _app.Widgets.FirstOrDefault();
        if (widget is null)
            return;
        SaveTokenIfDex(s);
        if (widget.Settings.Coins.Any(c => string.Equals(c, s.Key, StringComparison.OrdinalIgnoreCase)))
        {
            Feedback = Loc.F("Quick.AlreadyIn", widget.Name);
            return;
        }
        if (widget.Settings.Coins.Count >= SettingsViewModel.MaxCoins)
        {
            Feedback = Loc.F("Quick.Full", widget.Name);
            return;
        }
        widget.Settings.Coins.Add(s.Key);
        widget.ApplyCoinSettings();
        _app.Save();
        _app.RequestFeedRestart();
        s.IsAdded = true;
        Feedback = Loc.F("Quick.Added", widget.Name);
    }

    [RelayCommand]
    private void SetAlert()
    {
        if (Selected is not { } s)
            return;
        SaveTokenIfDex(s);
        _app.Alerts.NewSymbol = s.Key;
        _app.OpenSettingsPage("Alerts");
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void AddHolding()
    {
        if (Selected is not { } s)
            return;
        SaveTokenIfDex(s);
        _app.Portfolio.NewSymbol = s.Key;
        _app.OpenSettingsPage("Portfolio");
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the highlight in the results with the arrow keys.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
            return;
        var i = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[Math.Clamp(i + delta, 0, Results.Count - 1)];
    }

    // ---- Search ------------------------------------------------------------------------

    private async Task LoadCatalogAsync()
    {
        try
        {
            _coins = await _catalog.GetCoinsAsync(Quote, _lifetime.Token);
            Refresh();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Refresh()
    {
        var previous = Selected?.Key;
        Results.Clear();
        var inWidgets = _app.Widgets.SelectMany(w => w.Settings.Coins).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (Query.Trim().Length == 0)
        {
            // Nothing typed yet: the coins already on screen, for quick conversions.
            foreach (var key in inWidgets.Take(12))
                Results.Add(ForKey(key, inWidgets));
        }
        else
        {
            if (!CoinKey.LooksLikeAddress(Query))
            {
                foreach (var coin in CoinCatalogService.Search(_coins, Query, 8))
                    Results.Add(new CatalogItemViewModel(coin, _logos.GetLocal(coin.Symbol)) { IsAdded = inWidgets.Contains(coin.Symbol) });
            }
            foreach (var pair in _dexResults.Take(10))
                Results.Add(new CatalogItemViewModel(pair, _logos.GetLocal(pair.Key)) { IsAdded = inWidgets.Contains(pair.Key) });
        }

        foreach (var r in Results.Where(r => r.Logo is null))
            _ = LoadLogoAsync(r);

        Selected = Results.FirstOrDefault(r => r.Key == previous) ?? Results.FirstOrDefault();
        StatusText = Results.Count == 0 && Query.Trim().Length >= 2 && _dexSearch is null ? Loc.T("Quick.NothingFound") : StatusText;
    }

    private CatalogItemViewModel ForKey(string key, HashSet<string> inWidgets)
    {
        var info = CoinCatalog.Get(key);
        var subtitle = CoinKey.TryParseDex(key, out var chain, out _)
            ? DexChains.DisplayName(chain)
            : _coins.FirstOrDefault(c => c.Symbol == key) is { Exchanges.Count: > 0 } coin ? string.Join(" · ", coin.Exchanges) : "";
        return new CatalogItemViewModel(key, info.Name, subtitle, _logos.GetLocal(key)) { IsAdded = inWidgets.Contains(key) };
    }

    private async Task SearchDexAsync(string text)
    {
        _dexSearch?.Cancel();
        _dexSearch = null;
        text = text.Trim();
        if (text.Length < 2)
        {
            StatusText = "";
            return;
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _dexSearch = cts;
        StatusText = Loc.T("Coins.DexSearching");
        try
        {
            await Task.Delay(300, cts.Token);
            var pairs = await DexScreenerClient.SearchAsync(text, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            _dexResults = pairs;
            _dexSearch = null;
            StatusText = "";
            Refresh();
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            if (!cts.IsCancellationRequested)
            {
                _dexSearch = null;
                StatusText = Loc.T("Coins.DexFailed");
            }
        }
    }

    private async Task LoadLogoAsync(CatalogItemViewModel item)
    {
        try
        {
            item.Logo = item.Pair is { } pair
                ? await _logos.GetForUrlAsync(item.Key, pair.ImageUrl, _lifetime.Token)
                : await _logos.GetAsync(item.Key, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadSparklineAsync(string key)
    {
        try
        {
            var closes = await _app.History.GetAsync(new MarketSymbol(key, Quote), ChangePeriod.Day, _lifetime.Token);
            if (Selected?.Key == key)
                Sparkline = closes.Select(c => (double)c).ToArray();
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---- Live price --------------------------------------------------------------------

    private void OnPricesUpdated(object? sender, EventArgs e) => UpdatePrice();

    private bool TryGetPrice(out decimal price) => TryGetLatest(out price, out _);

    /// <summary>Price from the widgets' connection if it has the coin, otherwise from this window's own.</summary>
    private bool TryGetLatest(out decimal price, out decimal? change)
    {
        if (Selected?.Key is { } key)
        {
            if (_app.Alerts.TryGetLatest(key, out price, out change))
                return true;
            if (_own is { } own && own.Key == key)
            {
                (price, change) = (own.Price, own.Change);
                return true;
            }
        }
        (price, change) = (0, null);
        return false;
    }

    private void UpdatePrice()
    {
        if (Selected is null || !TryGetLatest(out var price, out var change))
            return;
        PriceText = CoinRowViewModel.FormatPrice(price, Quote);
        ChangeText = change is { } c ? CoinRowViewModel.FormatChange(c) + " · 24h" : "";
        IsUp = (change ?? 0) >= 0;
        if (ValueText.Length == 0)
            RecalculateValue();
    }

    private void RecalculateValue()
    {
        if (Selected is null || !TryGetPrice(out var price) || !PriceText_TryParse(AmountText, out var amount))
            return;
        _converting = true;
        ValueText = CoinRowViewModel.FormatNumber(amount * price).Replace(",", "");
        _converting = false;
    }

    private static bool PriceText_TryParse(string text, out decimal value) =>
        Core.Market.PriceText.TryParse(text, CultureInfo.CurrentCulture, out value) && value >= 0;

    private void SaveTokenIfDex(CatalogItemViewModel item)
    {
        if (item.Pair is { } pair)
            _app.AddToken(pair.ToToken());
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        StatusText = "";
        UpdatePrice();
    }

    public void Dispose()
    {
        StopOwnFeed();
        _selection?.Cancel();
        _app.PricesUpdated -= OnPricesUpdated;
        Loc.Changed -= OnLanguageChanged;
        _dexSearch?.Cancel();
        _lifetime.Cancel();
    }
}
