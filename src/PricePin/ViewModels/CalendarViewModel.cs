using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Events;
using PricePin.Core.Pulse;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>One row of the calendar.</summary>
public sealed partial class CalendarEventViewModel : ObservableObject
{
    public CalendarEventViewModel(MarketEvent e, bool showDay)
    {
        Event = e;
        ShowDay = showDay;
    }

    public MarketEvent Event { get; }

    /// <summary>True for the first event of each day, which shows the day above it.</summary>
    public bool ShowDay { get; }

    public string Title => CalendarViewModel.TitleFor(Event);

    /// <summary>"USD", or the coin for crypto events.</summary>
    public string Badge => Event.Country ?? (Event.Coins.Count > 0 ? Event.Coins[0] : "");
    public bool HasBadge => Badge.Length > 0;

    public string DayText => CalendarViewModel.DayText(Event.Time.ToLocalTime());

    public string TimeText => Event.Estimated
        ? "~" + Event.Time.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)
        : Event.Time.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    /// <summary>"Forecast 0.3% · Previous 0.2%" when the feed has them.</summary>
    public string DetailText => string.Join("  ·  ", new[]
    {
        Event.Forecast is { } f ? Loc.F("Calendar.Forecast", f) : null,
        Event.Previous is { } p ? Loc.F("Calendar.Previous", p) : null,
    }.OfType<string>());

    public bool HasDetail => DetailText.Length > 0;
    public bool HasLink => Event.Url is not null;

    [ObservableProperty] private string _countdownText = "";

    /// <summary>Starts within the hour: shown in the accent colour.</summary>
    [ObservableProperty] private bool _isSoon;

    public void Refresh(DateTimeOffset now)
    {
        var until = Event.Time - now;
        CountdownText = CalendarViewModel.Countdown(until);
        IsSoon = until > TimeSpan.Zero && until <= TimeSpan.FromHours(1);
    }

    [RelayCommand]
    private void Open()
    {
        if (Event.Url is { } url)
            PulseViewModel.OpenLink(url);
    }
}

/// <summary>The next event, for a widget footer or a ticker bar.</summary>
public sealed partial class NextEventViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasEvent;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _countdownText = "";
    [ObservableProperty] private bool _isSoon;
    [ObservableProperty] private string _toolTip = "";
}

/// <summary>
/// The Calendar tab: this week's big economic releases, known Fed and CPI dates further out, and the
/// next Bitcoin halving, with countdowns and an optional reminder before each one starts.
/// It only downloads while something needs it: the window, a widget's countdown, or reminders.
/// </summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);

    /// <summary>Events that started this long ago still show, marked as started.</summary>
    public static readonly TimeSpan KeepStarted = TimeSpan.FromMinutes(30);

    private const int MaxRows = 60;

    private readonly AppViewModel _app;
    private readonly Func<bool> _windowOpen;
    private readonly EventCalendarService _service = new();
    private readonly HashSet<string> _reminded = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private EventCalendar? _calendar;
    private DateTime _loadedAt = DateTime.MinValue;
    private bool _loading;

    public CalendarViewModel(AppViewModel app, Func<bool> windowOpen)
    {
        _app = app;
        _windowOpen = windowOpen;
        _allCountries = Settings.CalendarAllCountries;
        RemindOptions = PulseSettings.RemindOptions
            .Select(m => new ChoiceOption(m.ToString(CultureInfo.InvariantCulture), () => RemindLabel(m), o => RemindMinutes = int.Parse(o.Value, CultureInfo.InvariantCulture))
            {
                IsSelected = m == Settings.CalendarRemindMinutes,
            })
            .ToList();
    }

    private PulseSettings Settings => _app.Settings.Pulse;

    /// <summary>Raised for a reminder: title and body.</summary>
    public event Action<string, string>? NotificationRequested;

    /// <summary>Raised when the next event changes, so ticker bars can rebuild their strip.</summary>
    public event EventHandler? NextChanged;

    public ObservableCollection<CalendarEventViewModel> Events { get; } = [];
    public NextEventViewModel Next { get; } = new();

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasHalving;
    [ObservableProperty] private string _halvingCountdown = "";
    [ObservableProperty] private string _halvingDate = "";
    [ObservableProperty] private string _halvingDetail = "";

    /// <summary>Show every economy's releases, not just US ones.</summary>
    [ObservableProperty]
    private bool _allCountries;

    partial void OnAllCountriesChanged(bool value)
    {
        Settings.CalendarAllCountries = value;
        _app.Save();
        Show();
    }

    public IReadOnlyList<ChoiceOption> RemindOptions { get; }

    public int RemindMinutes
    {
        get => Settings.CalendarRemindMinutes;
        set
        {
            if (Settings.CalendarRemindMinutes == value)
                return;
            Settings.CalendarRemindMinutes = value;
            foreach (var o in RemindOptions)
                o.IsSelected = o.Value == value.ToString(CultureInfo.InvariantCulture);
            // Events already inside the new lead time shouldn't all fire at once.
            MarkAlreadyDue();
            _app.Save();
            OnPropertyChanged();
            OnTick();
        }
    }

    private bool Needed => _windowOpen() || RemindMinutes > 0 || _app.Widgets.Any(w => w.ShowEvents);

    /// <summary>Called every few seconds by the Pulse timer.</summary>
    public void OnTick()
    {
        if (Needed && !_loading && DateTime.UtcNow - _loadedAt >= RefreshEvery)
            _ = LoadAsync();
        RefreshCountdowns();
        CheckReminders();
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
            var first = _calendar is null;
            _calendar = await _service.FetchAsync(_lifetime.Token);
            _loadedAt = DateTime.UtcNow;
            if (first)
                MarkAlreadyDue();
            Status = _calendar.Errors.Count == 0 ? "" : Loc.F("Calendar.SourcesDown", string.Join(", ", _calendar.Errors));
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

    /// <summary>The events the user asked to see, soonest first.</summary>
    private List<MarketEvent> Visible(DateTimeOffset now) =>
        _calendar?.Events
            .Where(e => e.Time >= now - KeepStarted)
            .Where(e => AllCountries || e.Country is null || e.Country == "USD")
            .Take(MaxRows)
            .ToList() ?? [];

    private void Show()
    {
        var now = DateTimeOffset.UtcNow;
        Events.Clear();
        DateTime? lastDay = null;
        foreach (var e in Visible(now))
        {
            var day = e.Time.ToLocalTime().Date;
            Events.Add(new CalendarEventViewModel(e, day != lastDay));
            lastDay = day;
        }
        if (_calendar is null)
            Status = Loc.T("Calendar.Loading");
        else if (Events.Count == 0 && Status.Length == 0)
            Status = Loc.T("Calendar.Empty");

        if (_calendar?.Halving is { } h)
        {
            HasHalving = true;
            HalvingDate = Loc.F("Calendar.HalvingDate", h.Estimated.ToLocalTime().ToString("D", CultureInfo.CurrentCulture));
            HalvingDetail = Loc.F("Calendar.HalvingDetail",
                h.HalvingHeight.ToString("N0", CultureInfo.CurrentCulture),
                h.BlocksLeft.ToString("N0", CultureInfo.CurrentCulture));
        }
        RefreshCountdowns();
    }

    private void RefreshCountdowns()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var e in Events)
            e.Refresh(now);
        if (_calendar?.Halving is { } h)
            HalvingCountdown = Countdown(h.Estimated - now, daysOnly: true);

        // The widget shows the next event that hasn't started yet; the halving only when nothing else is near.
        var next = Visible(now).FirstOrDefault(e => e.Time > now && e.Kind != EventKind.Halving)
                   ?? Visible(now).FirstOrDefault(e => e.Time > now);
        var hadEvent = Next.HasEvent;
        var oldTitle = Next.Title;
        Next.HasEvent = next is not null;
        if (next is not null)
        {
            Next.Title = TitleFor(next);
            Next.CountdownText = Countdown(next.Time - now);
            Next.IsSoon = next.Time - now <= TimeSpan.FromHours(1);
            Next.ToolTip = $"{Next.Title}\n{DayText(next.Time.ToLocalTime())} {next.Time.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}";
        }
        if (hadEvent != Next.HasEvent || oldTitle != Next.Title)
            NextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CheckReminders()
    {
        if (RemindMinutes <= 0 || _calendar is null)
            return;
        var now = DateTimeOffset.UtcNow;
        foreach (var e in EventReminders.Due(Visible(now), now, TimeSpan.FromMinutes(RemindMinutes), _reminded))
        {
            var body = string.Join("  ·  ", new[]
            {
                e.Time.ToLocalTime().ToString("t", CultureInfo.CurrentCulture),
                e.Forecast is { } f ? Loc.F("Calendar.Forecast", f) : null,
                e.Previous is { } p ? Loc.F("Calendar.Previous", p) : null,
            }.OfType<string>());
            NotificationRequested?.Invoke(Loc.F("Calendar.RemindTitle", TitleFor(e), Countdown(e.Time - now)), body);
        }
    }

    /// <summary>Events already inside the reminder window count as reminded, so turning reminders on is quiet.</summary>
    private void MarkAlreadyDue()
    {
        if (_calendar is null || RemindMinutes <= 0)
            return;
        var now = DateTimeOffset.UtcNow;
        EventReminders.Due(_calendar.Events, now, TimeSpan.FromMinutes(RemindMinutes), _reminded);
    }

    public void RefreshText()
    {
        foreach (var o in RemindOptions)
            o.RefreshText();
        Show();
    }

    public void Stop() => _lifetime.Cancel();

    // ---- Text --------------------------------------------------------------------------

    /// <summary>Our own event names in the user's language; the weekly feed's names stay as they are.</summary>
    public static string TitleFor(MarketEvent e) => e.Kind == EventKind.Halving ? Loc.T("Calendar.Halving")
        : e.Source == "PricePin" && e.Title == "FOMC rate decision" ? Loc.T("Calendar.Fomc")
        : e.Source == "PricePin" && e.Title == "US CPI (inflation)" ? Loc.T("Calendar.Cpi")
        : e.Title;

    /// <summary>"in 2d 4h", "in 3h 12m", "in 8m", "now", "started".</summary>
    public static string Countdown(TimeSpan until, bool daysOnly = false)
    {
        if (until <= TimeSpan.Zero)
            return until > -TimeSpan.FromMinutes(2) ? Loc.T("Calendar.Now") : Loc.T("Calendar.Started");
        if (daysOnly && until.TotalDays >= 2)
            return Loc.F("Calendar.InDaysOnly", (int)until.TotalDays);
        return until.TotalDays >= 1 ? Loc.F("Calendar.InDays", (int)until.TotalDays, until.Hours)
            : until.TotalHours >= 1 ? Loc.F("Calendar.InHours", (int)until.TotalHours, until.Minutes)
            : Loc.F("Calendar.InMinutes", Math.Max(1, (int)Math.Ceiling(until.TotalMinutes)));
    }

    /// <summary>"Today", "Tomorrow", or "Wednesday 18 Nov".</summary>
    public static string DayText(DateTimeOffset local)
    {
        var today = DateTime.Now.Date;
        return local.Date == today ? Loc.T("Calendar.Today")
            : local.Date == today.AddDays(1) ? Loc.T("Calendar.Tomorrow")
            : local.ToString("dddd d MMM", CultureInfo.CurrentCulture);
    }

    private static string RemindLabel(int minutes) => minutes switch
    {
        0 => Loc.T("Calendar.RemindOff"),
        60 => Loc.T("Calendar.Remind1h"),
        _ => Loc.F("Calendar.RemindMinutes", minutes),
    };
}
