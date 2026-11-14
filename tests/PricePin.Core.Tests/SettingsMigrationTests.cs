using System.Text.Json;
using System.Text.Json.Serialization;
using PricePin.Core.Market;
using PricePin.Core.Settings;

namespace PricePin.Core.Tests;

public class SettingsMigrationTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void Version1_file_becomes_one_widget()
    {
        var json = """
            {"SchemaVersion":1,"Topmost":false,"Opacity":0.5,"ClickThrough":true,"LockPosition":true,"SnapToEdges":false,
             "StartWithWindows":true,"Left":100,"Top":200,"Layout":"List","Theme":"Light","Accent":"#3B82F6",
             "Size":"Large","ShowNames":false,"ShowSparklines":false,"ShowColumnHeadings":false,
             "Coins":["btc","SUI"],"DataSource":"Kraken","QuoteCurrency":"eur"}
            """;
        var s = JsonSerializer.Deserialize<AppSettings>(json, Options)!;
        s.Normalize();

        var w = Assert.Single(s.Widgets);
        Assert.False(w.Topmost);
        Assert.Equal(0.5, w.Opacity);
        Assert.True(w.ClickThrough);
        Assert.True(w.LockPosition);
        Assert.False(w.SnapToEdges);
        Assert.Equal(100, w.Left);
        Assert.Equal(WidgetSize.Large, w.Size);
        Assert.False(w.ShowNames);
        Assert.Equal(["BTC", "SUI"], w.Coins);
        Assert.Equal(DataSource.Kraken, s.DataSource);
        Assert.Equal("EUR", s.QuoteCurrency);
        Assert.Equal(AppTheme.Light, s.Theme);
        Assert.True(s.StartWithWindows);
        Assert.Equal(2, s.SchemaVersion);

        // Old top-level fields are not written back.
        var written = JsonSerializer.Serialize(s, Options);
        using var doc = JsonDocument.Parse(written);
        Assert.False(doc.RootElement.TryGetProperty("Coins", out _));
        Assert.False(doc.RootElement.TryGetProperty("Left", out _));
        Assert.False(doc.RootElement.TryGetProperty("Topmost", out _));
    }

    [Fact]
    public void Fresh_settings_get_a_default_widget()
    {
        var s = new AppSettings();
        s.Normalize();
        var w = Assert.Single(s.Widgets);
        Assert.Equal(WidgetLayout.List, w.Layout);
        Assert.NotEmpty(w.Coins);
    }

    [Fact]
    public void Bad_values_are_clamped_and_duplicate_ids_fixed()
    {
        var s = new AppSettings
        {
            Widgets =
            [
                new WidgetSettings { Id = "a", Opacity = 5, ScrollSpeed = -1, Name = "  " },
                new WidgetSettings { Id = "a", Opacity = double.NaN },
            ],
            Accent = "red",
        };
        s.Normalize();

        Assert.Equal(1.0, s.Widgets[0].Opacity);
        Assert.Equal(10, s.Widgets[0].ScrollSpeed);
        Assert.Equal("Widget", s.Widgets[0].Name);
        Assert.Equal(0.95, s.Widgets[1].Opacity);
        Assert.NotEqual(s.Widgets[0].Id, s.Widgets[1].Id);
        Assert.Equal(AccentPresets.Default, s.Accent);
    }
}
