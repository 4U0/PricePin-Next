using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Coins;
using PricePin.Core.Settings;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>A coin in "Your coins".</summary>
public sealed partial class CoinEntryViewModel(string symbol, string name) : ObservableObject
{
    public string Symbol { get; } = symbol;
    public string Name { get; } = name;
    public string Initial => Symbol[..1];

    [ObservableProperty]
    private ImageSource? _logo;
}

/// <summary>A search result in the coin picker.</summary>
public sealed partial class CatalogItemViewModel(CatalogCoin coin, ImageSource? logo) : ObservableObject
{
    public string Symbol { get; } = coin.Symbol;
    public string Name { get; } = coin.Name;
    public string Exchanges { get; } = coin.Exchanges.Count > 0 ? string.Join(" · ", coin.Exchanges) : "Not listed on supported exchanges";
    public ImageSource? Logo { get; } = logo;
    public string Initial => Symbol[..1];

    [ObservableProperty]
    private bool _isAdded;
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

        _ = LoadCatalogAsync();
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
            r.IsAdded = SelectedCoins.Any(c => c.Symbol == r.Symbol);
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

    public string CoinCountText => $"{SelectedCoins.Count} of {MaxCoins}";

    [ObservableProperty]
    private string _page = "General";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _catalogStatus = "Loading coin list…";

    /// <summary>Shown under the search box when the typed symbol isn't in any exchange list.</summary>
    [ObservableProperty]
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

    partial void OnSearchTextChanged(string value) => RefreshSearch();

    // ---- Coins -------------------------------------------------------------------------

    [RelayCommand]
    private void AddCoin(CatalogItemViewModel? item)
    {
        if (item is not null)
            Add(item.Symbol);
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
        symbol = symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0 || SelectedCoins.Count >= MaxCoins || SelectedCoins.Any(c => c.Symbol == symbol))
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
        SelectedWidget.Settings.Coins = SelectedCoins.Select(c => c.Symbol).ToList();
        _settings.ScheduleSave();
        SelectedWidget.ApplyCoinSettings();
        App.RequestFeedRestart();
        OnPropertyChanged(nameof(CoinCountText));
        foreach (var r in SearchResults)
            r.IsAdded = SelectedCoins.Any(c => c.Symbol == r.Symbol);
        UpdateCustomSymbol();
    }

    private CoinEntryViewModel CreateEntry(string symbol)
    {
        var name = _allCoins.FirstOrDefault(c => c.Symbol == symbol)?.Name ?? CoinCatalog.Get(symbol).Name;
        var entry = new CoinEntryViewModel(symbol, name) { Logo = _logos.GetLocal(symbol) };
        if (entry.Logo is null)
            _ = LoadLogoAsync(entry);
        return entry;
    }

    private async Task LoadLogoAsync(CoinEntryViewModel entry)
    {
        try
        {
            entry.Logo = await _logos.GetAsync(entry.Symbol, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadCatalogAsync()
    {
        var request = ++_catalogRequest;
        CatalogStatus = "Loading coin list…";
        try
        {
            var coins = await _catalog.GetCoinsAsync(QuoteCurrency, _lifetime.Token);
            if (request != _catalogRequest)
                return; // currency changed while loading
            _allCoins = coins;
            CatalogStatus = coins.Count > 0
                ? $"{coins.Count:N0} coins available in {QuoteCurrency}"
                : "Couldn't load the coin list. You can still add a coin by typing its symbol.";
            RefreshSearch();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RefreshSearch()
    {
        SearchResults.Clear();
        foreach (var coin in CoinCatalogService.Search(_allCoins, SearchText))
        {
            SearchResults.Add(new CatalogItemViewModel(coin, _logos.GetLocal(coin.Symbol))
            {
                IsAdded = SelectedCoins.Any(c => c.Symbol == coin.Symbol),
            });
        }
        UpdateCustomSymbol();
    }

    private void UpdateCustomSymbol()
    {
        var text = SearchText.Trim().ToUpperInvariant();
        CustomSymbol = text.Length is >= 2 and <= 12
                       && text.All(char.IsAsciiLetterOrDigit)
                       && SearchResults.All(r => r.Symbol != text)
                       && SelectedCoins.All(c => c.Symbol != text)
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

    public void Dispose() => _lifetime.Cancel();
}
