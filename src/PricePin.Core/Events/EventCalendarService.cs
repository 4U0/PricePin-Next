using System.Globalization;
using System.Reflection;
using System.Text.Json;
using PricePin.Core.Feeds;

namespace PricePin.Core.Events;

public enum EventKind { Macro, Crypto, Halving }

/// <summary>One thing on the calendar: a US data release, a Fed decision, a crypto event or the halving.</summary>
/// <param name="Country">Currency code of the economy it is about ("USD"), or null for crypto events.</param>
/// <param name="Coins">Coin symbols the event is about, if any.</param>
/// <param name="Estimated">True when the time is a forecast (the halving), not a fixed date.</param>
public sealed record MarketEvent(
    string Id,
    string Title,
    DateTimeOffset Time,
    EventKind Kind,
    string? Country,
    string? Forecast,
    string? Previous,
    string? Url,
    IReadOnlyList<string> Coins,
    string Source,
    bool Estimated = false);

/// <summary>When the next Bitcoin halving is expected, from the current block height.</summary>
public sealed record HalvingEstimate(long TipHeight, long HalvingHeight, DateTimeOffset Estimated, TimeSpan AverageBlock)
{
    public long BlocksLeft => HalvingHeight - TipHeight;
}

/// <summary>Everything the calendar shows. <see cref="Errors"/> lists sources that did not answer.</summary>
public sealed record EventCalendar(IReadOnlyList<MarketEvent> Events, HalvingEstimate? Halving, DateTimeOffset Time, IReadOnlyList<string> Errors);

/// <summary>
/// Upcoming market-moving events from free sources with no key:
/// <list type="bullet">
/// <item>This week's high-impact economic releases (CPI, jobs, Fed speakers) from the Forex Factory weekly feed.</item>
/// <item>A list of known dates further ahead (Fed decisions, CPI days) kept in <c>data/events.json</c> in the
/// PricePin repository. It is downloaded from GitHub, so it can be updated without a new release, and a
/// copy built into the app is used when GitHub can't be reached.</item>
/// <item>The next Bitcoin halving, estimated from mempool.space's block height and average block time.</item>
/// </list>
/// </summary>
public sealed class EventCalendarService
{
    public const string WeekUrl = "https://nfs.faireconomy.media/ff_calendar_thisweek.json";
    public const string CuratedUrl = "https://raw.githubusercontent.com/4U0/PricePin-Next/main/data/events.json";
    public const string TipHeightUrl = "https://mempool.space/api/blocks/tip/height";
    public const string DifficultyUrl = "https://mempool.space/api/v1/difficulty-adjustment";
    public const long HalvingInterval = 210_000;

    /// <summary>The weekly feed asks callers to be gentle, so it is read at most once an hour.</summary>
    public static readonly TimeSpan WeekCacheFor = TimeSpan.FromHours(1);
    public static readonly TimeSpan CuratedCacheFor = TimeSpan.FromHours(6);
    public static readonly TimeSpan HalvingCacheFor = TimeSpan.FromMinutes(30);

    /// <summary>A known date is dropped when the weekly feed has an event for the same economy this close to it.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(2);

    private (DateTimeOffset At, IReadOnlyList<MarketEvent> Events)? _week;
    private (DateTimeOffset At, IReadOnlyList<MarketEvent> Events)? _curated;
    private (DateTimeOffset At, HalvingEstimate Estimate)? _halving;

    public async Task<EventCalendar> FetchAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var errors = new List<string>();

        // Only sources whose copy is too old are asked again; the others answer from memory.
        var weekTask = _week is { } w && now - w.At < WeekCacheFor ? null : FetchWeekAsync(ct);
        var curatedTask = _curated is { } c && now - c.At < CuratedCacheFor ? null : FetchCuratedAsync(ct);
        var halvingTask = _halving is { } h && now - h.At < HalvingCacheFor ? null : FetchHalvingAsync(now, ct);
        await Task.WhenAll(new Task?[] { weekTask, curatedTask, halvingTask }.OfType<Task>()).ConfigureAwait(false);

        if (weekTask is not null)
        {
            if (weekTask.Result is { } fresh)
                _week = (now, fresh);
            else
                errors.Add("Forex Factory");
        }
        if (curatedTask?.Result is { } curatedFresh)
            _curated = (now, curatedFresh);
        if (halvingTask is not null)
        {
            if (halvingTask.Result is { } fresh)
                _halving = (now, fresh);
            else
                errors.Add("mempool.space");
        }

        // A built-in copy keeps the long-range dates working when GitHub can't be reached.
        var curated = _curated?.Events ?? LoadBundled();
        var events = Merge(_week?.Events ?? [], curated).ToList();
        var halving = _halving?.Estimate;
        if (halving is not null)
            events.Add(HalvingEvent(halving));
        return new EventCalendar(events.OrderBy(e => e.Time).ToList(), halving, now, errors);
    }

    // ---- Sources -----------------------------------------------------------------------

    internal static async Task<(IReadOnlyList<MarketEvent>? Events, string? Error)> FetchWeekWithErrorAsync(CancellationToken ct)
    {
        try
        {
            var text = await Http.Client.GetStringAsync(WeekUrl, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var events = ParseWeek(doc.RootElement);
            return (events, events is null ? "unexpected answer" : null);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<IReadOnlyList<MarketEvent>?> FetchWeekAsync(CancellationToken ct) =>
        (await FetchWeekWithErrorAsync(ct).ConfigureAwait(false)).Events;

    internal static async Task<(IReadOnlyList<MarketEvent>? Events, string? Error)> FetchCuratedWithErrorAsync(CancellationToken ct)
    {
        try
        {
            var text = await Http.Client.GetStringAsync(CuratedUrl, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var events = ParseCurated(doc.RootElement);
            return (events, events is null ? "unexpected answer" : null);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<IReadOnlyList<MarketEvent>?> FetchCuratedAsync(CancellationToken ct) =>
        (await FetchCuratedWithErrorAsync(ct).ConfigureAwait(false)).Events;

    internal static async Task<(HalvingEstimate? Estimate, string? Error)> FetchHalvingWithErrorAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var heightTask = Http.Client.GetStringAsync(TipHeightUrl, ct);
            var difficultyTask = Http.Client.GetStringAsync(DifficultyUrl, ct);
            await Task.WhenAll(heightTask, difficultyTask).ConfigureAwait(false);
            if (!long.TryParse(heightTask.Result.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var height) || height <= 0)
                return (null, "unexpected block height: " + Clip(heightTask.Result));
            using var doc = JsonDocument.Parse(difficultyTask.Result);
            var avg = Json.Decimal(doc.RootElement, "timeAvg") is { } ms and > 60_000 and < 3_600_000
                ? TimeSpan.FromMilliseconds((double)ms)
                : TimeSpan.FromMinutes(10);
            return (EstimateHalving(height, avg, now), null);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<HalvingEstimate?> FetchHalvingAsync(DateTimeOffset now, CancellationToken ct) =>
        (await FetchHalvingWithErrorAsync(now, ct).ConfigureAwait(false)).Estimate;

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException or InvalidOperationException or FormatException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private static string Clip(string s) => s.Length > 80 ? s[..80] : s;

    // ---- Parsing -----------------------------------------------------------------------

    // [{"title":"Core CPI m/m","country":"USD","date":"2026-11-17T08:30:00-05:00","impact":"High","forecast":"0.3%","previous":"0.2%"}, ...]
    /// <summary>The week's high-impact events. Holidays and lower-impact releases are left out.</summary>
    internal static IReadOnlyList<MarketEvent>? ParseWeek(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<MarketEvent>();
        foreach (var e in root.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object
                || !string.Equals(Json.String(e, "impact"), "High", StringComparison.OrdinalIgnoreCase)
                || Json.String(e, "title") is not { Length: > 0 } title
                || Json.String(e, "date") is not { } dateText
                || !DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                continue;
            var country = Json.String(e, "country")?.Trim().ToUpperInvariant();
            list.Add(new MarketEvent(
                Id: $"ff:{time.UtcDateTime:yyyyMMddHHmm}:{country}:{title}",
                Title: title.Trim(),
                Time: time.ToUniversalTime(),
                Kind: EventKind.Macro,
                Country: string.IsNullOrEmpty(country) ? null : country,
                Forecast: Blank(Json.String(e, "forecast")),
                Previous: Blank(Json.String(e, "previous")),
                Url: null,
                Coins: [],
                Source: "Forex Factory"));
        }
        return list;
    }

    // {"version":1,"events":[{"title":"FOMC rate decision","time":"2026-12-09T19:00:00Z","kind":"macro","country":"USD","url":"...","coins":["ETH"]}]}
    internal static IReadOnlyList<MarketEvent>? ParseCurated(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<MarketEvent>();
        foreach (var e in events.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object
                || Json.String(e, "title") is not { Length: > 0 } title
                || Json.String(e, "time") is not { } timeText
                || !DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
                continue;
            var kind = string.Equals(Json.String(e, "kind"), "crypto", StringComparison.OrdinalIgnoreCase) ? EventKind.Crypto : EventKind.Macro;
            var url = Json.String(e, "url");
            var coins = e.TryGetProperty("coins", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim().ToUpperInvariant())
                    .Where(x => x.Length is > 0 and <= 20).ToList()
                : [];
            list.Add(new MarketEvent(
                Id: $"pp:{time.UtcDateTime:yyyyMMddHHmm}:{title}",
                Title: title.Trim(),
                Time: time.ToUniversalTime(),
                Kind: kind,
                Country: Json.String(e, "country")?.Trim().ToUpperInvariant() is { Length: > 0 } country ? country : null,
                Forecast: null,
                Previous: null,
                // Only web links are opened.
                Url: url is not null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : null,
                Coins: coins,
                Source: "PricePin"));
        }
        return list;
    }

    /// <summary>The copy of <c>data/events.json</c> built into the app.</summary>
    internal static IReadOnlyList<MarketEvent> LoadBundled()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PricePin.Core.Events.events.json");
            if (stream is null)
                return [];
            using var doc = JsonDocument.Parse(stream);
            return ParseCurated(doc.RootElement) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// This week's live feed wins: a known date is dropped when the feed already lists an event for the
    /// same economy within <see cref="DuplicateWindow"/> (the feed calls the Fed decision "Federal Funds Rate").
    /// </summary>
    internal static IReadOnlyList<MarketEvent> Merge(IReadOnlyList<MarketEvent> week, IReadOnlyList<MarketEvent> curated)
    {
        var merged = new List<MarketEvent>(week);
        foreach (var e in curated)
        {
            var duplicate = e.Kind == EventKind.Macro && week.Any(w =>
                string.Equals(w.Country, e.Country, StringComparison.OrdinalIgnoreCase)
                && (w.Time - e.Time).Duration() <= DuplicateWindow
                && SameTopic(w.Title, e.Title));
            if (!duplicate)
                merged.Add(e);
        }
        return merged.OrderBy(e => e.Time).ToList();
    }

    /// <summary>Loose match between the feed's names and ours ("CPI m/m" and "US CPI (inflation)").</summary>
    private static bool SameTopic(string a, string b)
    {
        static string Topic(string t) =>
            t.Contains("CPI", StringComparison.OrdinalIgnoreCase) ? "cpi"
            : t.Contains("FOMC", StringComparison.OrdinalIgnoreCase) || t.Contains("Funds Rate", StringComparison.OrdinalIgnoreCase) ? "fomc"
            : t.ToLowerInvariant();
        return Topic(a) == Topic(b);
    }

    /// <summary>The next halving block and when it should arrive at the recent average block time.</summary>
    internal static HalvingEstimate EstimateHalving(long tipHeight, TimeSpan averageBlock, DateTimeOffset now)
    {
        var next = (tipHeight / HalvingInterval + 1) * HalvingInterval;
        var eta = now + TimeSpan.FromTicks(averageBlock.Ticks * (next - tipHeight));
        return new HalvingEstimate(tipHeight, next, eta, averageBlock);
    }

    internal static MarketEvent HalvingEvent(HalvingEstimate h) => new(
        Id: $"halving:{h.HalvingHeight}",
        Title: "Bitcoin halving",
        Time: h.Estimated,
        Kind: EventKind.Halving,
        Country: null,
        Forecast: null,
        Previous: null,
        Url: "https://mempool.space/",
        Coins: ["BTC"],
        Source: "mempool.space",
        Estimated: true);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Which events need a reminder now.</summary>
public static class EventReminders
{
    /// <summary>
    /// Events that start within <paramref name="lead"/> from now and haven't been reminded about yet.
    /// Each one is added to <paramref name="sent"/> so it is only reminded once.
    /// </summary>
    public static List<MarketEvent> Due(IEnumerable<MarketEvent> events, DateTimeOffset now, TimeSpan lead, ISet<string> sent)
    {
        var due = new List<MarketEvent>();
        if (lead <= TimeSpan.Zero)
            return due;
        foreach (var e in events)
        {
            var until = e.Time - now;
            if (until > TimeSpan.Zero && until <= lead && sent.Add(e.Id))
                due.Add(e);
        }
        return due;
    }
}
