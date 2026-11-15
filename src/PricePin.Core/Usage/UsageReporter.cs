using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PricePin.Core.Feeds;

namespace PricePin.Core.Usage;

/// <summary>
/// The anonymous "PricePin is running" ping that lets the maintainer count active users.
/// It is on by default with an off switch, sent only after the user has seen the notice about
/// it, at most once a day, to Aptabase
/// (privacy-first analytics: no IP addresses or device ids are stored). The event holds the
/// app version, the Windows version, the UI language and the update channel. Nothing about
/// the user's coins, alerts or holdings is ever sent.
/// </summary>
public sealed class UsageReporter
{
    public const string EventName = "app_started";

    private readonly string? _appKey;
    private readonly string _sessionId = NewSessionId();

    /// <param name="appKey">Aptabase app key ("A-EU-…" or "A-US-…"); null or empty turns reporting off.</param>
    public UsageReporter(string? appKey) => _appKey = string.IsNullOrWhiteSpace(appKey) ? null : appKey.Trim();

    /// <summary>The key baked in at build time (empty in local builds, so they never report).</summary>
    public static string? BuiltInKey(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "AptabaseKey")?.Value;

    /// <summary>False when this build has no key, e.g. a build from source.</summary>
    public bool IsAvailable => _appKey is not null && HostFor(_appKey) is not null;

    /// <summary>One ping per calendar day (UTC) is enough to count daily and monthly users.</summary>
    public static bool IsDue(DateTimeOffset? lastSent, DateTimeOffset now) =>
        lastSent is not { } last || last.UtcDateTime.Date != now.UtcDateTime.Date;

    internal static string? HostFor(string appKey)
    {
        var parts = appKey.Split('-');
        if (parts.Length != 3 || parts[0] != "A")
            return null;
        return parts[1] switch
        {
            "EU" => "https://eu.aptabase.com",
            "US" => "https://us.aptabase.com",
            _ => null, // self-hosted keys would need a host setting
        };
    }

    /// <summary>The exact JSON that is sent, so it can be tested and shown to the user.</summary>
    internal static string BuildPayload(string sessionId, DateTimeOffset now, string appVersion, string osVersion, string locale, string channel) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                timestamp = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                sessionId,
                eventName = EventName,
                systemProps = new
                {
                    isDebug = false,
                    osName = "Windows",
                    osVersion,
                    locale,
                    appVersion,
                    sdkVersion = "pricepin@1",
                },
                props = new { channel },
            },
        });

    /// <returns>True when the ping was accepted.</returns>
    public async Task<bool> SendAsync(string appVersion, string osVersion, string locale, string channel, CancellationToken ct = default)
    {
        if (_appKey is null || HostFor(_appKey) is not { } host)
            return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, host + "/api/v0/events");
            request.Headers.Add("App-Key", _appKey);
            request.Content = new StringContent(BuildPayload(_sessionId, DateTimeOffset.UtcNow, appVersion, osVersion, locale, channel),
                Encoding.UTF8, "application/json");
            using var response = await Http.Client.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // Aptabase's own SDKs use the epoch seconds plus random digits.
    private static string NewSessionId() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
}
