using System.Globalization;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PricePin.Core.Alerts;
using PricePin.Core.Coins;
using PricePin.Core.History;
using PricePin.Core.Listings;
using PricePin.Core.Market;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>
/// Smart alerts on the Alerts page: a coin moving fast, a coin trading far more than usual, and
/// new coins listed on the big exchanges. Moves are checked on every live price; volume every
/// five minutes for the coins on the widgets and in the portfolio; listings every half hour.
/// </summary>
public sealed partial class SmartAlertsViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan VolumeEvery = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ListingsEvery = TimeSpan.FromMinutes(30);

    /// <summary>One volume notification per coin in this time, however long the spike lasts.</summary>
    public static readonly TimeSpan VolumeQuiet = TimeSpan.FromHours(3);

    /// <summary>Volume is only checked for this many coins, so a long list doesn't hammer the exchanges.</summary>
    private const int MaxVolumeCoins = 25;

    private readonly AppViewModel _app;
    private readonly FastMoveDetector _moves = new();
    private readonly CandleService _candles = new();
    private readonly ListingService _listings;
    private readonly Dictionary<string, DateTimeOffset> _lastSpike = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private DateTime _volumeAt = DateTime.MinValue;
    private DateTime _listingsAt = DateTime.MinValue;
    private bool _volumeRunning;
    private bool _listingsRunning;

    public SmartAlertsViewModel(AppViewModel app)
    {
        _app = app;
        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PricePin");
        _listings = new ListingService(Path.Combine(localData, "catalog"));

        var s = Settings;
        _fastMove = s.FastMove;
        _volumeSpike = s.VolumeSpike;
        _newListings = s.NewListings;

        MoveOptions = SmartAlertSettings.MovePercents
            .Select(p => Option(p.ToString(CultureInfo.InvariantCulture), () => p.ToString("0.#", CultureInfo.CurrentCulture) + "%",
                p == s.FastMovePercent, o => Select(MoveOptions!, o, () => Settings.FastMovePercent = p)))
            .ToList();
        WindowOptions = SmartAlertSettings.MoveMinutes
            .Select(m => Option(m.ToString(CultureInfo.InvariantCulture), () => m == 60 ? Loc.T("Smart.OneHour") : Loc.F("Smart.Minutes", m),
                m == s.FastMoveMinutes, o => Select(WindowOptions!, o, () => Settings.FastMoveMinutes = m)))
            .ToList();
        SpikeOptions = SmartAlertSettings.SpikeFactors
            .Select(f => Option(f.ToString(CultureInfo.InvariantCulture), () => f.ToString("0", CultureInfo.CurrentCulture) + "×",
                f == s.VolumeSpikeFactor, o => Select(SpikeOptions!, o, () => Settings.VolumeSpikeFactor = f)))
            .ToList();

        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => OnTimer(), Dispatcher.CurrentDispatcher);
        _timer.Stop();
    }

    private SmartAlertSettings Settings => _app.Settings.SmartAlerts;

    /// <summary>Raised for a notification: title and body.</summary>
    public event Action<string, string>? NotificationRequested;

    public IReadOnlyList<ChoiceOption> MoveOptions { get; }
    public IReadOnlyList<ChoiceOption> WindowOptions { get; }
    public IReadOnlyList<ChoiceOption> SpikeOptions { get; }

    [ObservableProperty] private bool _fastMove;
    [ObservableProperty] private bool _volumeSpike;
    [ObservableProperty] private bool _newListings;

    partial void OnFastMoveChanged(bool value)
    {
        Settings.FastMove = value;
        _moves.Reset();
        _app.Save();
    }

    partial void OnVolumeSpikeChanged(bool value)
    {
        Settings.VolumeSpike = value;
        _app.Save();
        if (value)
            _volumeAt = DateTime.MinValue;
        OnTimer();
    }

    partial void OnNewListingsChanged(bool value)
    {
        Settings.NewListings = value;
        _app.Save();
        OnTimer();
    }

    public void Start()
    {
        _timer.Start();
        OnTimer();
    }

    // ---- Fast moves --------------------------------------------------------------------

    /// <summary>Takes each merged price batch from the feed.</summary>
    public void OnPrices(IEnumerable<Ticker> batch)
    {
        if (!FastMove)
            return;
        var mine = _app.Pulse.MyCoins();
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromMinutes(Settings.FastMoveMinutes);
        foreach (var t in batch)
        {
            if (!mine.Contains(t.Base))
                continue;
            if (_moves.Add(t.Base, t.Last, now, Settings.FastMovePercent, window) is { } move)
                NotifyMove(move);
        }
    }

    private void NotifyMove(FastMove move)
    {
        var quote = _app.Settings.QuoteCurrency;
        var symbol = CoinCatalog.DisplaySymbol(move.Symbol);
        var percent = Math.Abs(move.ChangePercent).ToString("0.#", CultureInfo.CurrentCulture);
        var window = Settings.FastMoveMinutes == 60 ? Loc.T("Smart.OneHour") : Loc.F("Smart.Minutes", Settings.FastMoveMinutes);
        var title = Loc.F(move.ChangePercent >= 0 ? "Smart.MoveUpTitle" : "Smart.MoveDownTitle", symbol, percent, window);
        var body = Loc.F("Smart.MoveBody", CoinRowViewModel.FormatPrice(move.From, quote), CoinRowViewModel.FormatPrice(move.To, quote));
        NotificationRequested?.Invoke(title, body);
    }

    /// <summary>Prices restart from scratch when the currency changes.</summary>
    public void OnQuoteChanged() => _moves.Reset();

    // ---- Volume and listings -----------------------------------------------------------

    private void OnTimer()
    {
        var now = DateTime.UtcNow;
        if (VolumeSpike && !_volumeRunning && now - _volumeAt >= VolumeEvery)
            _ = CheckVolumeAsync();
        if (NewListings && !_listingsRunning && now - _listingsAt >= ListingsEvery)
            _ = CheckListingsAsync();
    }

    private async Task CheckVolumeAsync()
    {
        _volumeRunning = true;
        _volumeAt = DateTime.UtcNow;
        try
        {
            var quote = _app.Settings.QuoteCurrency;
            var coins = _app.Pulse.MyCoins().Where(c => !CoinKey.IsDex(c)).Take(MaxVolumeCoins).ToList();
            foreach (var coin in coins)
            {
                if (!VolumeSpike || _lifetime.IsCancellationRequested)
                    break;
                var now = DateTimeOffset.UtcNow;
                if (_lastSpike.TryGetValue(coin, out var last) && now - last < VolumeQuiet)
                    continue;
                // The 1D chart's 15-minute candles, from whichever exchange has the coin.
                var series = await _candles.GetAsync(new MarketSymbol(coin, quote), ChartRange.Day, _lifetime.Token);
                if (series is null || VolumeSpikes.Check(coin, series.Candles, Settings.VolumeSpikeFactor, now) is not { } spike)
                    continue;
                _lastSpike[coin] = now;
                var symbol = CoinCatalog.DisplaySymbol(coin);
                var ratio = spike.Ratio.ToString("0.#", CultureInfo.CurrentCulture);
                var change = (spike.ChangePercent >= 0 ? "+" : "") + spike.ChangePercent.ToString("0.##", CultureInfo.CurrentCulture) + "%";
                NotificationRequested?.Invoke(Loc.F("Smart.VolumeTitle", symbol, ratio), Loc.F("Smart.VolumeBody", ratio, change, series.Source));
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _volumeRunning = false;
        }
    }

    private async Task CheckListingsAsync()
    {
        _listingsRunning = true;
        _listingsAt = DateTime.UtcNow;
        try
        {
            var found = await _listings.CheckAsync(_lifetime.Token);
            if (!NewListings || found.Count == 0)
                return;
            if (found.Count == 1)
            {
                var one = found[0];
                var name = CoinCatalog.IsKnown(one.Symbol) ? CoinCatalog.Get(one.Symbol).Name : one.Symbol;
                NotificationRequested?.Invoke(Loc.F("Smart.ListingTitle", one.Exchange, one.Symbol), Loc.F("Smart.ListingBody", name, one.Exchange));
            }
            else
            {
                var body = string.Join("  ·  ", found.GroupBy(f => f.Exchange)
                    .Select(g => g.Key + ": " + string.Join(", ", g.Select(f => f.Symbol).Take(6)) + (g.Count() > 6 ? "…" : "")));
                NotificationRequested?.Invoke(Loc.F("Smart.ListingsTitle", found.Count), body);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _listingsRunning = false;
        }
    }

    // ---- Helpers -----------------------------------------------------------------------

    private static ChoiceOption Option(string value, Func<string> label, bool selected, Action<ChoiceOption> select) =>
        new(value, label, select) { IsSelected = selected };

    private void Select(IReadOnlyList<ChoiceOption> options, ChoiceOption chosen, Action apply)
    {
        foreach (var o in options)
            o.IsSelected = o == chosen;
        apply();
        _moves.Reset();
        _app.Save();
    }

    public void RefreshText()
    {
        foreach (var o in MoveOptions.Concat(WindowOptions).Concat(SpikeOptions))
            o.RefreshText();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _timer.Stop();
    }
}
