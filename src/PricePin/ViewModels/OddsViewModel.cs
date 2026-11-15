using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Coins;
using PricePin.Core.News;
using PricePin.Core.Odds;
using PricePin.Core.Pulse;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>One answer under a market: "↑ 120,000   34%".</summary>
public sealed class OddsOutcomeViewModel(OddsOutcome o)
{
    public string Label => o.Label;
    public string ChanceText => OddsViewModel.Percent(o.Chance);

    /// <summary>Bar length, 0 to 1.</summary>
    public double Fill => (double)o.Chance;
    public string ChangeText => OddsViewModel.Points(o.Change24h);
    public bool? IsUp => OddsViewModel.Direction(o.Change24h);
}

/// <summary>One market in the odds list.</summary>
public sealed partial class OddsMarketViewModel : ObservableObject
{
    private readonly OddsViewModel _owner;

    public OddsMarketViewModel(OddsMarket market, bool pinned, OddsViewModel owner)
    {
        Market = market;
        _owner = owner;
        _isPinned = pinned;
        Outcomes = market.IsYesNo ? [] : market.Highlights(4).Select(o => new OddsOutcomeViewModel(o)).ToList();
    }

    public OddsMarket Market { get; }
    public string Title => Market.Title;
    public bool IsYesNo => Market.IsYesNo;

    /// <summary>The big number of a yes-or-no question.</summary>
    public string ChanceText => OddsViewModel.Percent(Market.Lead.Chance);
    public string ChangeText => OddsViewModel.Points(Market.Lead.Change24h);
    public bool? IsUp => OddsViewModel.Direction(Market.Lead.Change24h);

    public IReadOnlyList<OddsOutcomeViewModel> Outcomes { get; }

    /// <summary>"BTC · Polymarket · $1.2M today · ends in 5d".</summary>
    public string MetaText => OddsViewModel.Meta(Market);

    [ObservableProperty] private bool _isPinned;

    public string PinToolTip => Loc.T(IsPinned ? "Odds.Unpin" : "Odds.Pin");

    partial void OnIsPinnedChanged(bool value) => OnPropertyChanged(nameof(PinToolTip));

    [RelayCommand]
    private void TogglePin() => _owner.SetPinned(this, !IsPinned);

    [RelayCommand]
    private void Open() => PulseViewModel.OpenLink(Market.Url);
}

/// <summary>The first pinned (or busiest) market, for a widget footer or a ticker bar.</summary>
public sealed partial class OddsSummaryViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasMarket;
    [ObservableProperty] private string _title = "";

    /// <summary>"34%", or "↑ 120,000 34%" for a market with several answers.</summary>
    [ObservableProperty] private string _chanceText = "";
    [ObservableProperty] private string _changeText = "";
    [ObservableProperty] private bool? _isUp;
    [ObservableProperty] private string _toolTip = "";
}

/// <summary>
/// The Odds tab: what Polymarket and Kalshi traders give crypto questions ("Bitcoin above $150k this year?"),
/// with pinning, a widget line for the first pinned market, and a notification when a pinned one swings.
/// It only downloads while the window is open, a widget shows odds, or a pinned market is being watched.
/// </summary>
public sealed partial class OddsViewModel : ObservableObject
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(5);

    private readonly AppViewModel _app;
    private readonly Func<bool> _windowOpen;
    private readonly Func<IReadOnlyList<NewsCoin>> _coins;
    private readonly OddsService _service = new();
    private readonly OddsMoveWatcher _watcher = new();
    private readonly CancellationTokenSource _lifetime = new();
    private OddsBoard? _board;
    private DateTime _loadedAt = DateTime.MinValue;
    private bool _loading;

    public OddsViewModel(AppViewModel app, Func<bool> windowOpen, Func<IReadOnlyList<NewsCoin>> coins)
    {
        _app = app;
        _windowOpen = windowOpen;
        _coins = coins;
        _onlyMyCoins = Settings.OddsOnlyMyCoins;
        _notifyMoves = Settings.NotifyOddsMoves;
        _status = Loc.T("Odds.Loading");
    }

    private PulseSettings Settings => _app.Settings.Pulse;

    /// <summary>Raised for a notification: title and body.</summary>
    public event Action<string, string>? NotificationRequested;

    /// <summary>Raised when the widget summary changes, so ticker bars can rebuild their strip.</summary>
    public event EventHandler? SummaryChanged;

    public ObservableCollection<OddsMarketViewModel> Markets { get; } = [];
    public OddsSummaryViewModel Summary { get; } = new();

    [ObservableProperty] private string _status = "";

    [ObservableProperty] private bool _onlyMyCoins;

    partial void OnOnlyMyCoinsChanged(bool value)
    {
        Settings.OddsOnlyMyCoins = value;
        _app.Save();
        Show();
    }

    [ObservableProperty] private bool _notifyMoves;

    partial void OnNotifyMovesChanged(bool value)
    {
        Settings.NotifyOddsMoves = value;
        _app.Save();
        OnTick();
    }

    private bool Needed => _windowOpen()
                           || _app.Widgets.Any(w => w.ShowOdds)
                           || (NotifyMoves && Settings.PinnedOdds.Count > 0);

    /// <summary>Called every few seconds by the Pulse timer.</summary>
    public void OnTick()
    {
        if (Needed && !_loading && DateTime.UtcNow - _loadedAt >= RefreshEvery)
            _ = LoadAsync();
    }

    [RelayCommand]
    public Task Reload()
    {
        _loadedAt = DateTime.MinValue;
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading)
            return;
        _loading = true;
        try
        {
            var board = await _service.FetchAsync(_coins(), _lifetime.Token);
            _loadedAt = DateTime.UtcNow;
            // Keep the last good list when both sources fail for a moment.
            if (board.Markets.Count > 0 || _board is null)
                _board = board;
            Status = board.Errors.Count == 0 ? ""
                : board.Errors.Count == 2 ? Loc.T("Odds.Offline")
                : Loc.F("Odds.SourceDown", string.Join(", ", board.Errors));
            CheckMoves();
            Show();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _loading = false;
        }
    }

    private void Show()
    {
        var pinned = Settings.PinnedOdds;
        var mine = _app.Pulse.MyCoins();
        var all = _board?.Markets ?? [];
        var visible = all
            .Where(m => pinned.Contains(m.Id) || !OnlyMyCoins || m.Coins.Any(mine.Contains))
            .OrderBy(m => pinned.Contains(m.Id) ? pinned.IndexOf(m.Id) : int.MaxValue)
            .ToList();
        Markets.Clear();
        foreach (var m in visible)
            Markets.Add(new OddsMarketViewModel(m, pinned.Contains(m.Id), this));

        if (_board is null)
            Status = Loc.T("Odds.Loading");
        else if (Markets.Count == 0 && all.Count > 0 && Status.Length == 0)
            Status = Loc.T("Odds.NoneForMyCoins");
        else if (Markets.Count > 0 && Status == Loc.T("Odds.NoneForMyCoins"))
            Status = "";
        UpdateSummary();
    }

    public void SetPinned(OddsMarketViewModel row, bool pinned)
    {
        var list = Settings.PinnedOdds;
        list.Remove(row.Market.Id);
        if (pinned)
        {
            if (list.Count >= PulseSettings.MaxPinnedOdds)
                list.RemoveAt(list.Count - 1);
            list.Add(row.Market.Id);
            // The swing is measured from now, not from whenever the list first loaded.
            _watcher.Forget(row.Market.Id);
            _watcher.Check(row.Market);
        }
        _app.Save();
        Show();
        OnTick();
    }

    /// <summary>Notifies about pinned markets whose odds moved 10 points or more since last time.</summary>
    private void CheckMoves()
    {
        if (_board is null)
            return;
        var pinned = Settings.PinnedOdds;
        foreach (var m in _board.Markets.Where(m => pinned.Contains(m.Id)))
        {
            var moved = _watcher.Check(m);
            if (!NotifyMoves || moved.Count == 0)
                continue;
            var (outcome, from) = moved.OrderByDescending(x => Math.Abs(x.Outcome.Chance - x.From)).First();
            var what = m.IsYesNo ? "" : outcome.Label + ": ";
            NotificationRequested?.Invoke(
                Loc.F("Odds.MoveTitle", m.Title),
                Loc.F("Odds.MoveBody", what, Percent(from), Percent(outcome.Chance), m.Source));
        }
    }

    private void UpdateSummary()
    {
        var pinned = Settings.PinnedOdds;
        var markets = _board?.Markets ?? [];
        var m = pinned.Select(id => markets.FirstOrDefault(x => x.Id == id)).OfType<OddsMarket>().FirstOrDefault()
                ?? markets.FirstOrDefault();
        var had = Summary.HasMarket;
        var oldTitle = Summary.Title;
        Summary.HasMarket = m is not null;
        if (m is not null)
        {
            var lead = m.Lead;
            Summary.Title = m.Title;
            Summary.ChanceText = m.IsYesNo ? Percent(lead.Chance) : $"{lead.Label}  {Percent(lead.Chance)}";
            Summary.ChangeText = Points(lead.Change24h);
            Summary.IsUp = Direction(lead.Change24h);
            Summary.ToolTip = m.Title + "\n" + string.Join("\n", (m.IsYesNo ? m.Outcomes : m.Highlights(4)).Select(o =>
                (m.IsYesNo ? Loc.T("Odds.Yes") : o.Label) + "  " + Percent(o.Chance))) + "\n" + Meta(m);
        }
        if (had != Summary.HasMarket || oldTitle != Summary.Title)
            SummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshText()
    {
        Show();
    }

    public void Stop() => _lifetime.Cancel();

    // ---- Text --------------------------------------------------------------------------

    /// <summary>"34%", "&lt;1%", "&gt;99%".</summary>
    public static string Percent(decimal chance)
    {
        var p = chance * 100;
        return p < 1 && p > 0 ? "<1%" : p > 99 && p < 100 ? ">99%" : Math.Round(p).ToString("0", CultureInfo.CurrentCulture) + "%";
    }

    /// <summary>"+5 pts" or "−3 pts" over the last day; empty when it barely moved or isn't known.</summary>
    public static string Points(decimal? change)
    {
        if (change is not { } c || Math.Abs(c) < 0.005m)
            return "";
        var points = Math.Round(Math.Abs(c) * 100);
        return Loc.F("Odds.Points", (c > 0 ? "+" : "−") + points.ToString("0", CultureInfo.CurrentCulture));
    }

    public static bool? Direction(decimal? change) =>
        change is { } c && Math.Abs(c) >= 0.005m ? c > 0 : null;

    public static string Meta(OddsMarket m)
    {
        var parts = new List<string>();
        if (m.Coins.Count > 0)
            parts.Add(string.Join(" ", m.Coins.Take(2).Select(CoinCatalog.DisplaySymbol)));
        parts.Add(m.Source);
        if (m.Volume24hUsd >= 1_000)
            parts.Add(Loc.F("Odds.Volume", RadarText.Usd(m.Volume24hUsd)));
        if (m.Ends is { } end)
        {
            var left = end - DateTimeOffset.UtcNow;
            parts.Add(left.TotalDays < 2
                ? Loc.F("Odds.Ends", CalendarViewModel.Countdown(left))
                : Loc.F("Odds.EndsOn", end.ToLocalTime().ToString(end.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture)));
        }
        return string.Join("  ·  ", parts);
    }
}
