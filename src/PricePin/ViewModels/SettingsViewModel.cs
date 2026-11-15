using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Coins;
using PricePin.Core.Dex;
using PricePin.Core.Settings;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>A coin in "Your coins".</summary>
public sealed partial class CoinEntryViewModel(string key, string name) : ObservableObject
{
    /// <summary>The coin key ("BTC" or "dex:chain:address").</summary>
    public string Key { get; } = key;
    public string Symbol { get; } = CoinCatalog.DisplaySymbol(key);
    public string Name { get; } = name;
    public string Initial => Symbol.Length > 0 ? Symbol[..1] : "?";

    [ObservableProperty]
    private ImageSource? _logo;
}

/// <summary>A search result in the coin picker: an exchange coin, or a DEX token from DexScreener.</summary>
public sealed partial class CatalogItemViewModel : ObservableObject
{
    public CatalogItemViewModel(CatalogCoin coin, ImageSource? logo)
    {
        Key = coin.Symbol;
        Symbol = coin.Symbol;
        Name = coin.Name;
        Exchanges = coin.Exchanges.Count > 0 ? string.Join(" · ", coin.Exchanges) : Loc.T("Coins.NotListed");
        _logo = logo;
    }

    /// <summary>A coin the user already has, by key.</summary>
    public CatalogItemViewModel(string key, string name, string subtitle, ImageSource? logo)
    {
        Key = key;
        Symbol = CoinCatalog.DisplaySymbol(key);
        Name = name;
        Exchanges = subtitle;
        _logo = logo;
    }

    public CatalogItemViewModel(DexPair pair, ImageSource? logo)
    {
        Pair = pair;
        Key = pair.Key;
        Symbol = pair.BaseSymbol;
        Name = pair.BaseName;
        Exchanges = DexSummary(pair);
        _logo = logo;
    }

    public string Key { get; }
    public string Symbol { get; }
    public string Name { get; }

    /// <summary>"Binance · Coinbase", or "Solana · raydium · Liquidity $1.2M" for a DEX token.</summary>
    public string Exchanges { get; }

    /// <summary>The pool a DEX result was found in; null for exchange coins.</summary>
    public DexPair? Pair { get; }

    public bool IsDex => Pair is not null;
    public string Initial => Symbol.Length > 0 ? Symbol[..1] : "?";

    [ObservableProperty]
    private ImageSource? _logo;

    [ObservableProperty]
    private bool _isAdded;

    public static string DexSummary(DexPair p)
    {
        var parts = new List<string> { DexChains.DisplayName(p.Chain) };
        if (p.DexId.Length > 0)
            parts.Add(p.DexId);
        if (p.LiquidityUsd is { } liq)
            parts.Add(Loc.F("Coins.DexLiquidity", "$" + CoinRowViewModel.FormatCompact(liq)));
        return string.Join(" · ", parts);
    }
}

public sealed partial class AccentOption(string name, string hex) : ObservableObject
{
    public string Name { get; } = name;
    public string Hex { get; } = hex;
    public Brush Brush { get; } = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

    [ObservableProperty]
    private bool _isSelected;

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>
/// The Settings window. Every change applies immediately (no Save button). Per-widget
/// settings bind straight to <see cref="SelectedWidget"/>, app-wide ones to <see cref="App"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public const int MaxCoins = 40;
    public static IReadOnlyList<string> QuoteCurrencies { get; } = ["USD", "USDT", "EUR", "TRY", "GBP"];

    private readonly SettingsService _settings;
    private readonly CoinCatalogService _catalog;
    private readonly LogoProvider _logos;
    private readonly ThemeService _theme;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<CatalogCoin> _allCoins = [];
    private int _catalogRequest;
    private CancellationTokenSource? _dexSearch;
    private IReadOnlyList<DexPair> _dexResults = [];

    public SettingsViewModel(AppViewModel app, SettingsService settings, CoinCatalogService catalog, LogoProvider logos, ThemeService theme)
    {
        App = app;
        _selectedWidget = app.Widgets[0];
        _settings = settings;
        _catalog = catalog;
        _logos = logos;
        _theme = theme;

        var s = settings.Current;
        _quoteCurrency = s.QuoteCurrency;
        _selectedTheme = s.Theme;

        SelectedCoins = new(_selectedWidget.Settings.Coins.Select(CreateEntry));
        AccentOptions = AccentPresets.All.Select(a => new AccentOption(a.Name, a.Hex)).ToList();
        foreach (var a in AccentOptions)
            a.IsSelected = string.Equals(a.Hex, s.Accent, StringComparison.OrdinalIgnoreCase);

        Loc.Changed += OnLanguageChanged;
        _ = LoadCatalogAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CatalogStatus));
        OnPropertyChanged(nameof(CoinCountText));
        OnPropertyChanged(nameof(AddCustomText));
        OnPropertyChanged(nameof(DexStatus));
    }

    public AppViewModel App { get; }

    /// <summary>The widget being edited on the Widgets and Coins pages.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemoveWidget))]
    private WidgetViewModel _selectedWidget;

    public bool CanRemoveWidget => App.Widgets.Count > 1;
    public bool CanAddWidget => App.Widgets.Count < AppSettings.MaxWidgets;

    partial void OnSelectedWidgetChanged(WidgetViewModel value)
    {
        if (value is null)
        {
            SelectedWidget = App.Widgets[0];
            return;
        }
        SelectedCoins.Clear();
        foreach (var symbol in value.Settings.Coins)
            SelectedCoins.Add(CreateEntry(symbol));
        OnPropertyChanged(nameof(CoinCountText));
        foreach (var r in SearchResults)
            r.IsAdded = SelectedCoins.Any(c => c.Key == r.Key);
        UpdateCustomSymbol();
    }

    [RelayCommand]
    private void AddListWidget() => AddWidget(WidgetLayout.List);

    [RelayCommand]
    private void AddTickerBar() => AddWidget(WidgetLayout.TickerBar);

    [RelayCommand]
    private void RemoveWidget()
    {
        var doomed = SelectedWidget;
        if (App.Widgets.Count <= 1)
            return;
        SelectedWidget = App.Widgets.First(w => w != doomed);
        App.RemoveWidget(doomed);
        RefreshWidgetButtons();
    }

    private void AddWidget(WidgetLayout layout)
    {
        if (App.AddWidget(layout) is { } added)
            SelectedWidget = added;
        RefreshWidgetButtons();
    }

    private void RefreshWidgetButtons()
    {
        OnPropertyChanged(nameof(CanRemoveWidget));
        OnPropertyChanged(nameof(CanAddWidget));
    }

    public ObservableCollection<CoinEntryViewModel> SelectedCoins { get; }
    public ObservableCollection<CatalogItemViewModel> SearchResults { get; } = [];
    public IReadOnlyList<AccentOption> AccentOptions { get; }

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "dev";

    public string CoinCountText => Loc.F("Coins.Count", SelectedCoins.Count, MaxCoins);

    /// <summary>"Add XYZ anyway" for a symbol that isn't in any exchange's list.</summary>
    public string AddCustomText => CustomSymbol is null ? "" : Loc.F("Coins.AddAnyway", CustomSymbol);

    /// <summary>Coins in the loaded list: null while loading, 0 if loading failed.</summary>
    private int? _catalogCount;

    public string CatalogStatus => _catalogCount switch
    {
        null => Loc.T("Coins.Loading"),
        0 => Loc.T("Coins.LoadFailed"),
        var n => Loc.F("Coins.Loaded", n, QuoteCurrency),
    };

    /// <summary>State of the DEX token search under the results; empty when idle.</summary>
    public string DexStatus => _dexState switch
    {
        DexSearchState.Searching => Loc.T("Coins.DexSearching"),
        DexSearchState.Failed => Loc.T("Coins.DexFailed"),
        DexSearchState.None => Loc.T("Coins.DexNone"),
        _ => Loc.T("Coins.DexHint"),
    };

    private enum DexSearchState { Idle, Searching, Failed, None, Found }

    private DexSearchState _dexState;

    private void SetDexState(DexSearchState state)
    {
        _dexState = state;
        OnPropertyChanged(nameof(DexStatus));
    }

    [ObservableProperty]
    private string _page = "General";

    [ObservableProperty]
    private string _searchText = "";


    /// <summary>Shown under the search box when the typed symbol isn't in any exchange list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddCustomText))]
    private string? _customSymbol;

    [ObservableProperty]
    private string _quoteCurrency;

    [ObservableProperty]
    private AppTheme _selectedTheme;

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        _settings.Current.Theme = value;
        _settings.ScheduleSave();
        _theme.Apply(value, _settings.Current.Accent);
    }

    partial void OnQuoteCurrencyChanged(string value)
    {
        _settings.Current.QuoteCurrency = value;
        _settings.ScheduleSave();
        App.ApplyQuoteCurrency();
        _ = LoadCatalogAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _dexResults = [];
        RefreshSearch();
        _ = SearchDexAsync(value);
    }

    /// <summary>Looks the text up on DexScreener too, after a short pause in typing.</summary>
    private async Task SearchDexAsync(string text)
    {
        _dexSearch?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _dexSearch = cts;
        text = text.Trim();
        if (text.Length < 2)
        {
            SetDexState(DexSearchState.Idle);
            return;
        }
        SetDexState(DexSearchState.Searching);
        try
        {
            await Task.Delay(350, cts.Token);
            var pairs = await DexScreenerClient.SearchAsync(text, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            _dexResults = pairs;
            SetDexState(pairs.Count > 0 ? DexSearchState.Found : DexSearchState.None);
            RefreshSearch();
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            // A timeout is a failure too; a newer search or closing the window is not.
            if (!cts.IsCancellationRequested)
                SetDexState(DexSearchState.Failed);
        }
    }

    // ---- Coins -------------------------------------------------------------------------

    [RelayCommand]
    private void AddCoin(CatalogItemViewModel? item)
    {
        if (item is null)
            return;
        if (item.Pair is { } pair)
            App.AddToken(pair.ToToken());
        Add(item.Key);
    }

    [RelayCommand]
    private void AddCustom()
    {
        if (CustomSymbol is { } symbol)
            Add(symbol);
    }

    [RelayCommand]
    private void RemoveCoin(CoinEntryViewModel? entry)
    {
        if (entry is null)
            return;
        SelectedCoins.Remove(entry);
        CommitCoins();
    }

    [RelayCommand]
    private void MoveUp(CoinEntryViewModel? entry) => Move(entry, -1);

    [RelayCommand]
    private void MoveDown(CoinEntryViewModel? entry) => Move(entry, +1);

    /// <summary>Drag-and-drop reorder from the view.</summary>
    public void MoveTo(CoinEntryViewModel entry, int newIndex)
    {
        var old = SelectedCoins.IndexOf(entry);
        newIndex = Math.Clamp(newIndex, 0, SelectedCoins.Count - 1);
        if (old < 0 || old == newIndex)
            return;
        SelectedCoins.Move(old, newIndex);
        CommitCoins();
    }

    [RelayCommand]
    private void ResetCoins()
    {
        SelectedCoins.Clear();
        foreach (var symbol in CoinCatalog.Defaults)
            SelectedCoins.Add(CreateEntry(symbol));
        CommitCoins();
    }

    private void Add(string symbol)
    {
        symbol = CoinKey.Normalize(symbol);
        if (symbol.Length == 0 || SelectedCoins.Count >= MaxCoins
            || SelectedCoins.Any(c => string.Equals(c.Key, symbol, StringComparison.OrdinalIgnoreCase)))
            return;
        SelectedCoins.Add(CreateEntry(symbol));
        CommitCoins();
    }

    private void Move(CoinEntryViewModel? entry, int delta)
    {
        if (entry is not null)
            MoveTo(entry, SelectedCoins.IndexOf(entry) + delta);
    }

    private void CommitCoins()
    {
        SelectedWidget.Settings.Coins = SelectedCoins.Select(c => c.Key).ToList();
        _settings.ScheduleSave();
        SelectedWidget.ApplyCoinSettings();
        App.RequestFeedRestart();
        OnPropertyChanged(nameof(CoinCountText));
        foreach (var r in SearchResults)
            r.IsAdded = SelectedCoins.Any(c => c.Key == r.Key);
        UpdateCustomSymbol();
    }

    private CoinEntryViewModel CreateEntry(string symbol)
    {
        var name = CoinKey.TryParseDex(symbol, out var chain, out _)
            ? $"{CoinCatalog.Get(symbol).Name} · {DexChains.DisplayName(chain)}"
            : _allCoins.FirstOrDefault(c => c.Symbol == symbol)?.Name ?? CoinCatalog.Get(symbol).Name;
        var entry = new CoinEntryViewModel(symbol, name) { Logo = _logos.GetLocal(symbol) };
        if (entry.Logo is null)
            _ = LoadLogoAsync(entry);
        return entry;
    }

    private async Task LoadLogoAsync(CoinEntryViewModel entry)
    {
        try
        {
            entry.Logo = await _logos.GetAsync(entry.Key, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadCatalogAsync()
    {
        var request = ++_catalogRequest;
        _catalogCount = null;
        OnPropertyChanged(nameof(CatalogStatus));
        try
        {
            var coins = await _catalog.GetCoinsAsync(QuoteCurrency, _lifetime.Token);
            if (request != _catalogRequest)
                return; // currency changed while loading
            _allCoins = coins;
            _catalogCount = coins.Count;
            OnPropertyChanged(nameof(CatalogStatus));
            RefreshSearch();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RefreshSearch()
    {
        SearchResults.Clear();
        IReadOnlyList<CatalogCoin> exchange = CoinKey.LooksLikeAddress(SearchText) ? [] : CoinCatalogService.Search(_allCoins, SearchText);
        foreach (var coin in exchange)
        {
            SearchResults.Add(new CatalogItemViewModel(coin, _logos.GetLocal(coin.Symbol))
            {
                IsAdded = SelectedCoins.Any(c => c.Key == coin.Symbol),
            });
        }
        // DEX tokens after the exchange coins: a coin on Binance is usually the one people mean.
        foreach (var pair in _dexResults)
        {
            var item = new CatalogItemViewModel(pair, _logos.GetLocal(pair.Key))
            {
                IsAdded = SelectedCoins.Any(c => string.Equals(c.Key, pair.Key, StringComparison.OrdinalIgnoreCase)),
            };
            SearchResults.Add(item);
            if (item.Logo is null)
                _ = LoadResultLogoAsync(item);
        }
        UpdateCustomSymbol();
    }

    private async Task LoadResultLogoAsync(CatalogItemViewModel item)
    {
        try
        {
            item.Logo = await _logos.GetForUrlAsync(item.Key, item.Pair?.ImageUrl, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateCustomSymbol()
    {
        var text = SearchText.Trim().ToUpperInvariant();
        CustomSymbol = text.Length is >= 2 and <= 12
                       && text.All(char.IsAsciiLetterOrDigit)
                       && !CoinKey.LooksLikeAddress(SearchText)
                       && SearchResults.All(r => r.IsDex || r.Symbol != text)
                       && SelectedCoins.All(c => c.Key != text)
            ? text
            : null;
    }

    // ---- Appearance --------------------------------------------------------------------

    [RelayCommand]
    private void SelectAccent(AccentOption? option)
    {
        if (option is null)
            return;
        foreach (var a in AccentOptions)
            a.IsSelected = a == option;
        _settings.Current.Accent = option.Hex;
        _settings.ScheduleSave();
        _theme.ApplyAccent(option.Hex);
    }

    // ---- Data & about ------------------------------------------------------------------

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        var folder = Path.GetDirectoryName(_settings.FilePath);
        if (folder is not null)
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenProjectPage() =>
        Process.Start(new ProcessStartInfo("https://github.com/4U0/PricePin-Next") { UseShellExecute = true });

    public void Dispose()
    {
        _dexSearch?.Cancel();
        Loc.Changed -= OnLanguageChanged;
        _lifetime.Cancel();
    }
}
