using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Market;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>A coin in the "your coins today" lists of the market overview.</summary>
public sealed record MoverItem(string Key, string Symbol, string PriceText, string ChangeText, bool IsUp);

/// <summary>
/// The market overview window: total market cap and its 24h change, BTC and ETH dominance,
/// the Fear &amp; Greed Index, and the best and worst of the user's own coins today.
/// </summary>
public sealed partial class MarketViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(5);

    private readonly AppViewModel _app;
    private readonly MarketOverviewService _service;
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _movers;
    private readonly CancellationTokenSource _lifetime = new();
    private MarketOverview? _overview;

    public MarketViewModel(AppViewModel app, MarketOverviewService service)
    {
        _app = app;
        _service = service;
        _refresh = new DispatcherTimer(RefreshEvery, DispatcherPriority.Background, async (_, _) => await LoadAsync(), Dispatcher.CurrentDispatcher);
        _movers = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => UpdateMovers(), Dispatcher.CurrentDispatcher);
        Loc.Changed += OnLanguageChanged;
        UpdateMovers();
        _ = LoadAsync();
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string _marketCapText = "—";

    [ObservableProperty]
    private string _marketCapChangeText = "";

    [ObservableProperty]
    private bool _marketCapUp = true;

    [ObservableProperty]
    private string _btcDominanceText = "—";

    [ObservableProperty]
    private string _ethDominanceText = "—";

    /// <summary>0–100, or null when the index couldn't be loaded.</summary>
    [ObservableProperty]
    private int? _fearGreed;

    [ObservableProperty]
    private string _fearGreedLabel = "";

    [ObservableProperty]
    private string _updatedText = "";

    public ObservableCollection<MoverItem> Gainers { get; } = [];
    public ObservableCollection<MoverItem> Losers { get; } = [];

    [RelayCommand]
    private Task Reload() => LoadAsync(force: true);

    [RelayCommand]
    private void OpenCoin(string symbol) => _app.OpenCoinDetail(symbol);

    private async Task LoadAsync(bool force = false)
    {
        if (IsLoading)
            return;
        IsLoading = true;
        try
        {
            var overview = await _service.GetAsync(_app.Settings.QuoteCurrency, _lifetime.Token);
            if (overview.IsEmpty)
            {
                Message = Loc.T("Market.LoadFailed");
                return;
            }
            Message = null;
            _overview = overview;
            ApplyOverview();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyOverview()
    {
        if (_overview is not { } o)
            return;
        var quote = o.MarketCapCurrency.ToUpperInvariant();
        var (prefix, suffix) = CoinRowViewModel.CurrencyAffixes(quote);
        MarketCapText = o.TotalMarketCap is { } cap ? prefix + BigNumber(cap) + suffix : "—";
        MarketCapChangeText = o.MarketCapChange24h is { } ch ? Signed(ch) + " · 24h" : "";
        MarketCapUp = (o.MarketCapChange24h ?? 0) >= 0;
        BtcDominanceText = o.BtcDominance is { } b ? b.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "—";
        EthDominanceText = o.EthDominance is { } e ? e.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "—";
        FearGreed = o.FearGreed;
        FearGreedLabel = o.FearGreedLevel switch
        {
            FearGreedLevel.ExtremeFear => Loc.T("Market.ExtremeFear"),
            FearGreedLevel.Fear => Loc.T("Market.Fear"),
            FearGreedLevel.Neutral => Loc.T("Market.Neutral"),
            FearGreedLevel.Greed => Loc.T("Market.Greed"),
            FearGreedLevel.ExtremeGreed => Loc.T("Market.ExtremeGreed"),
            _ => "",
        };
        UpdatedText = Loc.F("Market.Updated", o.FetchedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture));
    }

    private void UpdateMovers()
    {
        var rows = _app.Widgets.SelectMany(w => w.Coins)
            .Where(r => r.HasPrice && r.ChangePercent24h is not null)
            .DistinctBy(r => r.Key)
            .OrderByDescending(r => r.ChangePercent24h)
            .ToList();
        Replace(Gainers, rows.Where(r => r.ChangePercent24h >= 0).Take(3));
        Replace(Losers, rows.AsEnumerable().Reverse().Where(r => r.ChangePercent24h < 0).Take(3));
    }

    private static void Replace(ObservableCollection<MoverItem> target, IEnumerable<CoinRowViewModel> rows)
    {
        var items = rows.Select(r => new MoverItem(r.Key, r.Symbol, r.PriceText, r.ChangeText24h, r.ChangePercent24h >= 0)).ToList();
        if (target.SequenceEqual(items))
            return;
        target.Clear();
        foreach (var i in items)
            target.Add(i);
    }

    public void Start()
    {
        _refresh.Start();
        _movers.Start();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyOverview();

    /// <summary>3.21T, 845.2B, 12.4M.</summary>
    internal static string BigNumber(decimal v) => Math.Abs(v) switch
    {
        >= 1_000_000_000_000m => (v / 1_000_000_000_000m).ToString("0.00", CultureInfo.InvariantCulture) + "T",
        >= 1_000_000_000m => (v / 1_000_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "B",
        >= 1_000_000m => (v / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M",
        _ => v.ToString("#,0", CultureInfo.InvariantCulture),
    };

    private static string Signed(decimal pct) =>
        (pct >= 0 ? "▲ " : "▼ ") + Math.Abs(pct).ToString("0.00", CultureInfo.InvariantCulture) + "%";

    public void Dispose()
    {
        Loc.Changed -= OnLanguageChanged;
        _refresh.Stop();
        _movers.Stop();
        _lifetime.Cancel();
    }
}
