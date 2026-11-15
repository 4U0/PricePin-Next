using System.Text.Json.Serialization;
using PricePin.Core.Alerts;
using PricePin.Core.Coins;
using PricePin.Core.Dex;
using PricePin.Core.Portfolio;
using PricePin.Core.Market;
using PricePin.Core.Pulse;

namespace PricePin.Core.Settings;

/// <summary>How a widget shows its coins.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetLayout>))]
public enum WidgetLayout
{
    /// <summary>A card with one row per coin.</summary>
    List,

    /// <summary>A horizontal strip that scrolls the coins past, like a stock ticker.</summary>
    TickerBar,
}

/// <summary>Where a ticker bar sits.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BarDock>))]
public enum BarDock
{
    Top,
    Bottom,

    /// <summary>Not docked: drag it anywhere.</summary>
    Floating,
}

[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    /// <summary>Follow the Windows light/dark app setting.</summary>
    System,
    Dark,
    Light,
}

[JsonConverter(typeof(JsonStringEnumConverter<WidgetSize>))]
public enum WidgetSize
{
    Small,
    Medium,
    Large,
}

/// <summary>One widget window. Users can have several, each with its own coins and look.</summary>
public sealed class WidgetSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Main";
    public WidgetLayout Layout { get; set; } = WidgetLayout.List;
    public bool Visible { get; set; } = true;

    /// <summary>Coin keys to show, in display order: symbols like "BTC", or "dex:chain:address" for DEX tokens.</summary>
    public List<string> Coins { get; set; } = [.. CoinCatalog.Defaults];

    // Window behaviour
    public bool Topmost { get; set; } = true;
    public double Opacity { get; set; } = 0.95;
    public bool ClickThrough { get; set; }
    public bool LockPosition { get; set; }
    public bool SnapToEdges { get; set; } = true;

    /// <summary>Position in device-independent units. Null means "pick a default spot".</summary>
    public double? Left { get; set; }
    public double? Top { get; set; }

    // Look
    public WidgetSize Size { get; set; } = WidgetSize.Medium;
    public bool ShowNames { get; set; } = true;
    public bool ShowSparklines { get; set; } = true;
    public bool ShowColumnHeadings { get; set; } = true;

    /// <summary>Time span of the change and sparkline: the last hour, day or week.</summary>
    public ChangePeriod ChangePeriod { get; set; } = ChangePeriod.Day;

    /// <summary>Show what the user holds of each coin, and the portfolio total, in a list widget.</summary>
    public bool ShowHoldings { get; set; } = true;

    // Ticker bar
    public BarDock Dock { get; set; } = BarDock.Top;

    /// <summary>Docked bars push other windows aside, like the taskbar.</summary>
    public bool ReserveSpace { get; set; }

    public bool Scroll { get; set; } = true;

    /// <summary>Scroll speed in device-independent pixels per second.</summary>
    public double ScrollSpeed { get; set; } = 45;

    /// <summary>Width of a floating bar.</summary>
    public double FloatingWidth { get; set; } = 720;

    /// <summary>Mix the latest headlines into a ticker bar, or show one under a list.</summary>
    public bool ShowNews { get; set; }

    /// <summary>Show Bitcoin and Ethereum network fees in the widget.</summary>
    public bool ShowFees { get; set; }

    /// <summary>Show a countdown to the next big market event in the widget.</summary>
    public bool ShowEvents { get; set; }

    /// <summary>Show the odds of the first pinned (or busiest) prediction market in the widget.</summary>
    public bool ShowOdds { get; set; }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N")[..8];
        if (string.IsNullOrWhiteSpace(Name)) Name = "Widget";
        Name = Name.Trim();
        if (Name.Length > 40) Name = Name[..40];
        if (!Enum.IsDefined(Layout)) Layout = WidgetLayout.List;
        if (!Enum.IsDefined(Size)) Size = WidgetSize.Medium;
        if (!Enum.IsDefined(Dock)) Dock = BarDock.Top;
        if (!Enum.IsDefined(ChangePeriod)) ChangePeriod = ChangePeriod.Day;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, OpacityLimits.Min, OpacityLimits.Max) : 0.95;
        ScrollSpeed = double.IsFinite(ScrollSpeed) ? Math.Clamp(ScrollSpeed, 10, 200) : 45;
        FloatingWidth = double.IsFinite(FloatingWidth) ? Math.Clamp(FloatingWidth, 320, 4000) : 720;
        Coins = (Coins ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(CoinKey.Normalize)
            .Distinct()
            .ToList();
        if (Left is { } l && !double.IsFinite(l)) Left = null;
        if (Top is { } t && !double.IsFinite(t)) Top = null;
    }
}

public sealed class HotkeySettings
{
    /// <summary>Shows or hides every widget.</summary>
    public string ToggleWidgets { get; set; } = "Ctrl+Alt+P";

    /// <summary>Turns click-through on or off for every widget.</summary>
    public string ToggleClickThrough { get; set; } = "Ctrl+Alt+T";

    /// <summary>Hides or shows portfolio amounts.</summary>
    public string TogglePrivacy { get; set; } = "Ctrl+Alt+H";

    /// <summary>Opens the quick search window.</summary>
    public string QuickSearch { get; set; } = "Ctrl+Alt+K";
}

/// <summary>
/// Everything the user can change. Saved as JSON in %AppData%\PricePin\settings.json.
/// New properties must have safe defaults so older settings files keep loading.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 2;
    public const int MaxWidgets = 8;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<WidgetSettings> Widgets { get; set; } = [];

    public bool StartWithWindows { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Accent colour as #RRGGBB.</summary>
    public string Accent { get; set; } = AccentPresets.Default;

    /// <summary>Exchange to stream from. Auto picks the first one that works where the user is.</summary>
    public DataSource DataSource { get; set; } = DataSource.Auto;

    /// <summary>Currency prices are shown in (all widgets).</summary>
    public string QuoteCurrency { get; set; } = "USD";

    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>UI language code ("en", "tr", ...) or "system" to follow Windows.</summary>
    public string Language { get; set; } = SystemLanguage;

    /// <summary>Check GitHub Releases for new versions and download them in the background.</summary>
    public bool AutoUpdate { get; set; } = true;

    public const string SystemLanguage = "system";
    public const int MaxAlerts = 100;

    public List<PriceAlert> Alerts { get; set; } = [];

    /// <summary>Play the Windows notification sound when an alert fires.</summary>
    public bool AlertSound { get; set; } = true;

    public const int MaxHoldings = 100;

    public List<Holding> Holdings { get; set; } = [];

    /// <summary>Hide holding amounts and values (for screen sharing and streaming).</summary>
    public bool PrivacyMode { get; set; }

    /// <summary>Coin whose price is drawn in the tray icon; empty shows the PricePin logo.</summary>
    public string TrayCoin { get; set; } = "";

    public const int MaxTokens = 200;

    /// <summary>DEX tokens the user added, so their names, logos and pools are known offline.</summary>
    public List<DexToken> Tokens { get; set; } = [];

    /// <summary>
    /// Send the anonymous daily usage ping. It is on by default, but null means the user hasn't
    /// seen the notice about it yet, and nothing is sent until they have.
    /// </summary>
    public bool? UsageStats { get; set; }

    /// <summary>When the last usage ping was accepted, so only one goes out a day.</summary>
    public DateTimeOffset? UsageLastSent { get; set; }

    /// <summary>News, whale and liquidation radar, network fees and the event calendar.</summary>
    public PulseSettings Pulse { get; set; } = new();

    /// <summary>Fast-move, volume-spike and new-listing alerts.</summary>
    public SmartAlertSettings SmartAlerts { get; set; } = new();

    // ---- Version 1 fields, read once to migrate and never written again ----
    [JsonPropertyName("Topmost"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? V1Topmost { get; set; }
    [JsonPropertyName("Opacity"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? V1Opacity { get; set; }
    [JsonPropertyName("ClickThrough"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? V1ClickThrough { get; set; }
    [JsonPropertyName("LockPosition"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? V1LockPosition { get; set; }
    [JsonPropertyName("SnapToEdges"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? V1SnapToEdges { get; set; }
    [JsonPropertyName("Left"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? V1Left { get; set; }
    [JsonPropertyName("Top"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? V1Top { get; set; }
    [JsonPropertyName("Size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WidgetSize? V1Size { get; set; }
    [JsonPropertyName("ShowNames"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? V1ShowNames { get; set; }
    [JsonPropertyName("ShowSparklines"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? V1ShowSparklines { get; set; }
    [JsonPropertyName("ShowColumnHeadings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? V1ShowColumnHeadings { get; set; }
    [JsonPropertyName("Coins"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? V1Coins { get; set; }

    /// <summary>Upgrades old files and clamps values that may have been hand-edited into something unusable.</summary>
    public void Normalize()
    {
        if (Widgets is null || Widgets.Count == 0)
            Widgets = [MigrateV1Widget()];
        ClearV1Fields();

        if (Widgets.Count > MaxWidgets)
            Widgets = Widgets.Take(MaxWidgets).ToList();
        foreach (var w in Widgets)
            w.Normalize();
        // Ids must be unique; duplicate ones (from hand editing) get fresh ids.
        foreach (var dup in Widgets.GroupBy(w => w.Id).SelectMany(g => g.Skip(1)))
            dup.Id = Guid.NewGuid().ToString("N")[..8];

        QuoteCurrency = string.IsNullOrWhiteSpace(QuoteCurrency) ? "USD" : QuoteCurrency.Trim().ToUpperInvariant();
        if (!Enum.IsDefined(DataSource)) DataSource = DataSource.Auto;
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.Dark;
        if (!AccentPresets.IsValid(Accent)) Accent = AccentPresets.Default;
        Hotkeys ??= new HotkeySettings();
        Alerts = (Alerts ?? []).Where(x => x is not null && x.IsValid).Take(MaxAlerts).ToList();
        foreach (var alert in Alerts)
        {
            alert.Symbol = CoinKey.Normalize(alert.Symbol);
            alert.Quote = alert.Quote.Trim().ToUpperInvariant();
        }
        foreach (var dup in Alerts.GroupBy(x => x.Id).SelectMany(g => g.Skip(1)))
            dup.Id = Guid.NewGuid().ToString("N")[..8];
        TrayCoin = string.IsNullOrWhiteSpace(TrayCoin) ? "" : CoinKey.Normalize(TrayCoin);
        Holdings = (Holdings ?? []).Where(h => h is not null && h.IsValid).Take(MaxHoldings).ToList();
        foreach (var h in Holdings)
        {
            h.Symbol = CoinKey.Normalize(h.Symbol);
            h.CostQuote = string.IsNullOrWhiteSpace(h.CostQuote) ? QuoteCurrency : h.CostQuote.Trim().ToUpperInvariant();
        }
        // One entry per coin: merge duplicates (amounts add up, cost becomes the weighted average).
        Holdings = Holdings.GroupBy(h => h.Symbol).Select(MergeHoldings).ToList();
        Tokens = (Tokens ?? []).Where(t => t is not null).ToList();
        foreach (var t in Tokens)
            t.Normalize();
        Tokens = Tokens.Where(t => t.IsValid).DistinctBy(t => t.Key).Take(MaxTokens).ToList();
        // Every window resolves token keys to names through the catalog.
        foreach (var t in Tokens)
            CoinCatalog.Register(t);
        Pulse ??= new PulseSettings();
        Pulse.Normalize();
        SmartAlerts ??= new SmartAlertSettings();
        SmartAlerts.Normalize();
        Language = string.IsNullOrWhiteSpace(Language) ? SystemLanguage : Language.Trim().ToLowerInvariant();
        SchemaVersion = CurrentSchemaVersion;
    }

    private static Holding MergeHoldings(IGrouping<string, Holding> group)
    {
        var first = group.First();
        if (!group.Skip(1).Any())
            return first;
        var amount = group.Sum(h => h.Amount);
        var costed = group.Where(h => h.AverageCost is not null && h.CostQuote == first.CostQuote).ToList();
        decimal? cost = costed.Count == group.Count() && amount > 0
            ? costed.Sum(h => h.AverageCost!.Value * h.Amount) / amount
            : null;
        return new Holding { Symbol = first.Symbol, Amount = amount, AverageCost = cost, CostQuote = first.CostQuote };
    }

    private WidgetSettings MigrateV1Widget()
    {
        var w = new WidgetSettings { Name = "Main" };
        if (V1Topmost is { } top) w.Topmost = top;
        if (V1Opacity is { } op) w.Opacity = op;
        if (V1ClickThrough is { } ct) w.ClickThrough = ct;
        if (V1LockPosition is { } lp) w.LockPosition = lp;
        if (V1SnapToEdges is { } snap) w.SnapToEdges = snap;
        w.Left = V1Left;
        w.Top = V1Top;
        if (V1Size is { } size) w.Size = size;
        if (V1ShowNames is { } sn) w.ShowNames = sn;
        if (V1ShowSparklines is { } ss) w.ShowSparklines = ss;
        if (V1ShowColumnHeadings is { } sh) w.ShowColumnHeadings = sh;
        if (V1Coins is { } coins) w.Coins = coins;
        return w;
    }

    private void ClearV1Fields()
    {
        V1Topmost = null; V1Opacity = null; V1ClickThrough = null; V1LockPosition = null; V1SnapToEdges = null;
        V1Left = null; V1Top = null; V1Size = null; V1ShowNames = null; V1ShowSparklines = null; V1ShowColumnHeadings = null;
        V1Coins = null;
    }
}

public static class AccentPresets
{
    public const string Default = "#22D399";

    /// <summary>Name and colour of each accent offered in Settings.</summary>
    public static IReadOnlyList<(string Name, string Hex)> All { get; } =
    [
        ("Emerald", "#22D399"),
        ("Sky", "#38BDF8"),
        ("Blue", "#3B82F6"),
        ("Violet", "#8B5CF6"),
        ("Pink", "#EC4899"),
        ("Amber", "#F59E0B"),
        ("Orange", "#F97316"),
        ("Slate", "#94A3B8"),
    ];

    public static bool IsValid(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit);
}

public static class OpacityLimits
{
    public const double Min = 0.2;
    public const double Max = 1.0;
}
