using System.Text.Json;
using PricePin.Core.Settings;
using PricePin.Core.Usage;

namespace PricePin.Core.Tests;

public class UsageTests
{
    [Fact]
    public void Sends_at_most_once_a_day()
    {
        var now = new DateTimeOffset(2026, 11, 15, 23, 30, 0, TimeSpan.Zero);
        Assert.True(UsageReporter.IsDue(null, now));
        Assert.False(UsageReporter.IsDue(now.AddHours(-20), now));
        Assert.True(UsageReporter.IsDue(now.AddDays(-1), now));
        // Days are counted in UTC, whatever the user's time zone.
        Assert.False(UsageReporter.IsDue(new DateTimeOffset(2026, 11, 16, 1, 0, 0, TimeSpan.FromHours(3)), now));
    }

    [Theory]
    [InlineData("A-EU-1234567890", "https://eu.aptabase.com")]
    [InlineData("A-US-1234567890", "https://us.aptabase.com")]
    [InlineData("A-SH-1234567890", null)]
    [InlineData("nonsense", null)]
    public void Picks_the_region_from_the_key(string key, string? host) =>
        Assert.Equal(host, UsageReporter.HostFor(key));

    [Fact]
    public void Builds_without_a_key_report_nothing()
    {
        Assert.False(new UsageReporter(null).IsAvailable);
        Assert.False(new UsageReporter("  ").IsAvailable);
        Assert.True(new UsageReporter("A-EU-123").IsAvailable);
    }

    [Fact]
    public void The_ping_holds_only_version_os_language_and_channel()
    {
        var json = UsageReporter.BuildPayload("1700000000123", new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero),
            "1.6.0", "10.0.26100", "tr-TR", "win-x64");
        var e = JsonDocument.Parse(json).RootElement[0];
        Assert.Equal("app_started", e.GetProperty("eventName").GetString());
        Assert.Equal("2026-11-15T12:00:00.000Z", e.GetProperty("timestamp").GetString());
        var sys = e.GetProperty("systemProps");
        Assert.Equal(["isDebug", "osName", "osVersion", "locale", "appVersion", "sdkVersion"], sys.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["channel"], e.GetProperty("props").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Nobody_is_counted_before_seeing_the_notice()
    {
        var s = new AppSettings();
        s.Normalize();
        Assert.Null(s.UsageStats);
    }
}
