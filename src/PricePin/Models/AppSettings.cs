using System.Text.Json.Serialization;
using PricePin.Core.Coins;
using PricePin.Core.Market;

namespace PricePin.Models;

/// <summary>Which widget layout is shown.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetLayout>))]
public enum WidgetLayout
{
    /// <summary>A card with one row per coin.</summary>
    List,

    /// <summary>A horizontal scrolling ticker bar (arrives in a later phase).</summary>
    TickerBar,
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

/// <summary>
/// Everything the user can change. Saved as JSON in %AppData%\PricePin\settings.json.
/// New properties must have safe defaults so older settings files keep loading.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // Window behaviour
    public bool Topmost { get; set; } = true;
    public double Opacity { get; set; } = 0.95;
    public bool ClickThrough { get; set; }
    public bool LockPosition { get; set; }
    public bool SnapToEdges { get; set; } = true;
    public bool StartWithWindows { get; set; }

    // Where the widget sits, in device-independent units. Null means "pick a default spot".
    public double? Left { get; set; }
    public double? Top { get; set; }

    public WidgetLayout Layout { get; set; } = WidgetLayout.List;

    // Appearance
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Accent colour as #RRGGBB.</summary>
    public string Accent { get; set; } = AccentPresets.Default;

    public WidgetSize Size { get; set; } = WidgetSize.Medium;
    public bool ShowNames { get; set; } = true;
    public bool ShowSparklines { get; set; } = true;
    public bool ShowColumnHeadings { get; set; } = true;

    /// <summary>Base symbols to show, in display order.</summary>
    public List<string> Coins { get; set; } = [.. CoinCatalog.Defaults];

    /// <summary>Exchange to stream from. Auto picks the first one that works where the user is.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DataSource>))]
    public DataSource DataSource { get; set; } = DataSource.Auto;

    /// <summary>Currency prices are shown in.</summary>
    public string QuoteCurrency { get; set; } = "USD";

    /// <summary>Clamps values that may have been hand-edited into something unusable.</summary>
    public void Normalize()
    {
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, OpacityLimits.Min, OpacityLimits.Max) : 0.95;
        Coins = (Coins ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        if (string.IsNullOrWhiteSpace(QuoteCurrency))
            QuoteCurrency = "USD";
        QuoteCurrency = QuoteCurrency.Trim().ToUpperInvariant();
        if (!Enum.IsDefined(DataSource))
            DataSource = DataSource.Auto;
        if (!Enum.IsDefined(Theme))
            Theme = AppTheme.Dark;
        if (!Enum.IsDefined(Size))
            Size = WidgetSize.Medium;
        if (!AccentPresets.IsValid(Accent))
            Accent = AccentPresets.Default;
        if (Left is { } l && !double.IsFinite(l)) Left = null;
        if (Top is { } t && !double.IsFinite(t)) Top = null;
        SchemaVersion = CurrentSchemaVersion;
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
