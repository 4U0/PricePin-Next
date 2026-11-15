using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Coins;
using PricePin.Core.Dex;
using PricePin.Core.History;
using PricePin.Core.Market;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>
/// The coin detail window: live price, a day/week/month/year chart that follows the live
/// price, the range's high, low and change, and shortcuts to set an alert or add a holding.
/// </summary>
public sealed partial class CoinDetailViewModel : ObservableObject, IDisposable
{
    private readonly AppViewModel _app;
    private readonly CandleService _candles;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<Candle> _loaded = [];
    private int _request;
    private DateTime _lastLiveRedraw;

    public CoinDetailViewModel(AppViewModel app, CandleService candles, string key)
    {
        _app = app;
        _candles = candles;
        var info = CoinCatalog.Get(key);
        Key = key;
        Symbol = info.Symbol;
        Name = CoinKey.TryParseDex(key, out var chain, out _) ? $"{info.Name} · {DexChains.DisplayName(chain)}" : info.Name;
        if (CoinCatalog.GetToken(key) is { PairAddress: { } pool } token)
            DexUrl = $"https://dexscreener.com/{Uri.EscapeDataString(token.Chain)}/{Uri.EscapeDataString(pool)}";
        BadgeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(info.BrandColor));
        BadgeBrush.Freeze();
        _logo = app.Logos.GetLocal(Key);
        Quote = app.Settings.QuoteCurrency;
        PriceFormatter = p => CoinRowViewModel.FormatPrice(p, Quote);

        app.PricesUpdated += OnPricesUpdated;
        app.Watch(Key);
        UpdateLive();
        _ = LoadLogoAsync();
        _ = LoadAsync();
    }

    /// <summary>The coin's key ("BTC" or "dex:chain:address").</summary>
    public string Key { get; }

    public string Symbol { get; }

    /// <summary>The token's page on DexScreener, for DEX tokens.</summary>
    public string? DexUrl { get; }

    public bool HasDexUrl => DexUrl is not null;

    [RelayCommand]
    private void OpenDexScreener()
    {
        if (DexUrl is not null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DexUrl) { UseShellExecute = true });
    }
    public string Name { get; }
    public string Initial => Symbol[..1];
    public Brush BadgeBrush { get; }
    public string Quote { get; }
    public Func<decimal, string> PriceFormatter { get; }
    public string Title => $"{Symbol} · {Name}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    private ImageSource? _logo;

    public bool HasLogo => Logo is not null;

    [ObservableProperty]
    private string _priceText = "—";

    [ObservableProperty]
    private string _changeText = "";

    [ObservableProperty]
    private bool _isUp = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDay))]
    private ChartRange _range = ChartRange.Day;

    public bool IsDay => Range == ChartRange.Day;

    [ObservableProperty]
    private IReadOnlyList<Candle>? _chart;

    [ObservableProperty]
    private bool _chartUp = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string _highText = "—";

    [ObservableProperty]
    private string _lowText = "—";

    [ObservableProperty]
    private string _rangeChangeText = "—";

    [ObservableProperty]
    private string _volumeText = "—";

    [ObservableProperty]
    private string _sourceText = "";

    partial void OnRangeChanged(ChartRange value) => _ = LoadAsync();

    [RelayCommand]
    private void SetAlert()
    {
        _app.Alerts.NewSymbol = Key;
        _app.OpenSettingsPage("Alerts");
    }

    [RelayCommand]
    private void AddHolding()
    {
        _app.Portfolio.NewSymbol = Key;
        _app.OpenSettingsPage("Portfolio");
    }

    [RelayCommand]
    private Task Reload() => LoadAsync();

    private async Task LoadAsync()
    {
        var request = ++_request;
        IsLoading = true;
        Message = null;
        try
        {
            var series = await _candles.GetAsync(new MarketSymbol(Key, Quote), Range, _lifetime.Token);
            if (request != _request)
                return; // the range changed while loading
            if (series is null)
            {
                _loaded = [];
                Chart = null;
                Message = Loc.F("Detail.NoData", Symbol, Quote);
                SourceText = "";
                UpdateStats();
                return;
            }
            _loaded = series.Candles;
            SourceText = Loc.F("Detail.Source", series.Source);
            ApplyLive(force: true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (request == _request)
                IsLoading = false;
        }
    }

    private void OnPricesUpdated(object? sender, EventArgs e)
    {
        UpdateLive();
        // Redraw the chart with the live price about once a second.
        if (DateTime.UtcNow - _lastLiveRedraw > TimeSpan.FromSeconds(1))
            ApplyLive(force: false);
    }

    private void UpdateLive()
    {
        if (!_app.Alerts.TryGetLatest(Key, out var price, out var change))
            return;
        PriceText = CoinRowViewModel.FormatPrice(price, Quote);
        ChangeText = change is { } c ? (c >= 0 ? "▲ " : "▼ ") + Math.Abs(c).ToString("0.00", CultureInfo.InvariantCulture) + "% · 24h" : "";
        IsUp = (change ?? 0) >= 0;
    }

    /// <summary>Puts the live price on the last candle so the chart ends where the price is now.</summary>
    private void ApplyLive(bool force)
    {
        if (_loaded.Count < 2)
            return;
        var candles = _loaded;
        if (_app.Alerts.TryGetLatest(Key, out var price, out _))
        {
            var last = candles[^1];
            if (!force && last.Close == price && Chart is not null)
                return;
            var list = candles.Take(candles.Count - 1).ToList();
            list.Add(last with { Close = price, High = Math.Max(last.High, price), Low = Math.Min(last.Low, price) });
            candles = list;
        }
        _lastLiveRedraw = DateTime.UtcNow;
        Chart = candles;
        ChartUp = candles[^1].Close >= candles[0].Open;
        UpdateStats();
    }

    private void UpdateStats()
    {
        var c = Chart;
        if (c is not { Count: > 1 })
        {
            HighText = LowText = RangeChangeText = VolumeText = "—";
            return;
        }
        HighText = CoinRowViewModel.FormatPrice(c.Max(x => x.High), Quote);
        LowText = CoinRowViewModel.FormatPrice(c.Min(x => x.Low), Quote);
        var first = c[0].Open;
        var pct = first > 0 ? (c[^1].Close - first) / first * 100m : 0;
        RangeChangeText = (pct >= 0 ? "+" : "−") + Math.Abs(pct).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        VolumeText = CoinRowViewModel.FormatCompact(c.Sum(x => x.Volume)) + " " + Symbol;
    }

    private async Task LoadLogoAsync()
    {
        if (Logo is not null)
            return;
        try
        {
            Logo = await _app.Logos.GetAsync(Key, _lifetime.Token);
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        _app.PricesUpdated -= OnPricesUpdated;
        _app.Unwatch(Key);
        _lifetime.Cancel();
    }
}
