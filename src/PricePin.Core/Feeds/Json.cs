using System.Globalization;
using System.Text.Json;

namespace PricePin.Core.Feeds;

/// <summary>Helpers for exchange JSON, which sends numbers sometimes as strings and sometimes as numbers.</summary>
internal static class Json
{
    public static decimal? Decimal(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var e))
            return null;
        return e.ValueKind switch
        {
            JsonValueKind.Number => e.TryGetDecimal(out var d) ? d : (decimal)e.GetDouble(),
            JsonValueKind.String when decimal.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    public static string? String(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    public static long? Int64(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var v) ? v : null;

    public static DateTimeOffset? UnixMs(JsonElement obj, string name) =>
        Int64(obj, name) is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    public static DateTimeOffset? Iso(JsonElement obj, string name) =>
        String(obj, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;
}
