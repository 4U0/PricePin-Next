using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PricePin.Core.Feeds;

namespace PricePin.Core.Icons;

/// <summary>
/// Finds a logo for any coin and caches it on disk:
/// 1. the CC0 cryptocurrency-icons set (fast CDN, covers most established coins),
/// 2. CoinGecko search (covers newer coins).
/// A coin with no logo anywhere is remembered for a week so we don't keep asking.
/// The app ships logos for popular coins, so this is only needed for the rest.
/// </summary>
public sealed class CoinIconService
{
    private static readonly TimeSpan MissingRetryAfter = TimeSpan.FromDays(7);
    private readonly string _cacheDir;
    private readonly SemaphoreSlim _throttle = new(3);

    public CoinIconService(string cacheDirectory)
    {
        _cacheDir = cacheDirectory;
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Path of an already-downloaded logo, without touching the network.</summary>
    public string? TryGetCachedPath(string symbol)
    {
        var key = symbol.ToLowerInvariant();
        if (key.Any(c => !char.IsAsciiLetterOrDigit(c)))
            return null;
        var path = Path.Combine(_cacheDir, key + ".png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Returns the path of a cached PNG, downloading it if needed, or null if none exists.</summary>
    public async Task<string?> GetIconPathAsync(string symbol, CancellationToken ct = default)
    {
        var key = symbol.ToLowerInvariant();
        if (key.Any(c => !char.IsAsciiLetterOrDigit(c)))
            return null;

        var path = Path.Combine(_cacheDir, key + ".png");
        if (File.Exists(path))
            return path;

        var missingMarker = Path.Combine(_cacheDir, key + ".missing");
        if (File.Exists(missingMarker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missingMarker) < MissingRetryAfter)
            return null;

        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (bytes, cdnSaysMissing) = await TryDownloadAsync(
                $"https://cdn.jsdelivr.net/gh/spothq/cryptocurrency-icons@v0.18.1/128/color/{key}.png", ct).ConfigureAwait(false);
            var geckoSaysMissing = false;
            if (bytes is null)
                (bytes, geckoSaysMissing) = await TryCoinGeckoAsync(key, ct).ConfigureAwait(false);

            if (bytes is null)
            {
                // Only remember "no logo" when both sources clearly said so, not after a network hiccup.
                if (cdnSaysMissing && geckoSaysMissing)
                    await File.WriteAllTextAsync(missingMarker, "", ct).ConfigureAwait(false);
                return null;
            }

            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException
                                   && !ct.IsCancellationRequested)
        {
            return null; // try again next launch
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>File a DEX token's logo is cached in; the key contains characters no file name may have.</summary>
    private string TokenPath(string key)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.ToLowerInvariant())));
        return Path.Combine(_cacheDir, "dex-" + hash[..24].ToLowerInvariant() + ".img");
    }

    /// <summary>Path of an already-downloaded DEX token logo, or null.</summary>
    public string? TryGetCachedTokenPath(string key)
    {
        var path = TokenPath(key);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Downloads a DEX token's logo from the URL DexScreener gave, and caches it.</summary>
    public async Task<string?> GetTokenIconPathAsync(string key, string? imageUrl, CancellationToken ct = default)
    {
        var path = TokenPath(key);
        if (File.Exists(path))
            return path;
        if (imageUrl is null || !imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (bytes, _) = await TryDownloadAsync(imageUrl, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length > 2_000_000)
                return null;
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <returns>The image, or null plus whether the server definitely has no such file (404).</returns>
    private static async Task<(byte[]? Bytes, bool Missing)> TryDownloadAsync(string url, CancellationToken ct)
    {
        using var response = await Http.Client.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return (null, true);
        if (!response.IsSuccessStatusCode)
            return (null, false);
        if (response.Content.Headers.ContentType?.MediaType is { } type && !type.StartsWith("image/", StringComparison.Ordinal))
            return (null, false);
        return (await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false), false);
    }

    private static async Task<(byte[]? Bytes, bool Missing)> TryCoinGeckoAsync(string symbol, CancellationToken ct)
    {
        using var response = await Http.Client.GetAsync($"https://api.coingecko.com/api/v3/search?query={symbol}", ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return (null, false); // includes rate limiting (429): try again another time
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
        var url = PickCoinGeckoImage(json, symbol);
        return url is null ? (null, true) : await TryDownloadAsync(url, ct).ConfigureAwait(false);
    }

    /// <summary>Picks the exact-symbol match with the best market-cap rank.</summary>
    internal static string? PickCoinGeckoImage(JsonElement search, string symbol)
    {
        if (!search.TryGetProperty("coins", out var coins))
            return null;

        return coins.EnumerateArray()
            .Where(c => string.Equals(Json.String(c, "symbol"), symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.TryGetProperty("market_cap_rank", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : int.MaxValue)
            .Select(c => Json.String(c, "large") ?? Json.String(c, "thumb"))
            .FirstOrDefault(u => u is not null && u.StartsWith("https://", StringComparison.Ordinal));
    }
}
