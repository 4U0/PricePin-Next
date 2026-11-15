using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PricePin.Core.Coins;
using PricePin.Core.Icons;

namespace PricePin.Services;

/// <summary>
/// Coin logos as WPF images: bundled ones first, then the on-disk cache, then a download.
/// Each logo is decoded once and shared by the widget and the settings window.
/// </summary>
public sealed class LogoProvider(CoinIconService icons)
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Logo available without any network request, or null.</summary>
    public ImageSource? GetLocal(string symbol)
    {
        if (_cache.TryGetValue(symbol, out var cached) && cached is not null)
            return cached;

        var image = CoinKey.IsDex(symbol)
            ? icons.TryGetCachedTokenPath(symbol) is { } tokenPath ? Load(new Uri(tokenPath)) : null
            : TryLoadBundled(symbol) ?? (icons.TryGetCachedPath(symbol) is { } path ? Load(new Uri(path)) : null);
        if (image is not null)
            _cache[symbol] = image;
        return image;
    }

    /// <summary>Logo for a DEX search result that isn't saved yet.</summary>
    public async Task<ImageSource?> GetForUrlAsync(string key, string? imageUrl, CancellationToken ct)
    {
        if (GetLocal(key) is { } local)
            return local;
        var path = await icons.GetTokenIconPathAsync(key, imageUrl, ct);
        var image = path is null ? null : Load(new Uri(path));
        if (image is not null)
            _cache[key] = image;
        return image;
    }

    /// <summary>Logo from anywhere, downloading if needed. Null if the coin has none.</summary>
    public async Task<ImageSource?> GetAsync(string symbol, CancellationToken ct)
    {
        if (GetLocal(symbol) is { } local)
            return local;

        var path = CoinKey.IsDex(symbol)
            ? await icons.GetTokenIconPathAsync(symbol, CoinCatalog.GetToken(symbol)?.ImageUrl, ct)
            : await icons.GetIconPathAsync(symbol, ct);
        var image = path is null ? null : Load(new Uri(path));
        if (image is not null)
            _cache[symbol] = image;
        return image;
    }

    private static ImageSource? TryLoadBundled(string symbol)
    {
        if (symbol.Any(c => !char.IsAsciiLetterOrDigit(c)))
            return null;
        var uri = new Uri($"pack://application:,,,/Assets/Coins/{symbol.ToLowerInvariant()}.png");
        try
        {
            using var stream = Application.GetResourceStream(uri)?.Stream;
            return stream is null ? null : Load(uri);
        }
        catch (IOException)
        {
            return null; // not bundled
        }
    }

    private static ImageSource? Load(Uri uri)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.DecodePixelWidth = 64; // shown at ~30px; 64 stays sharp up to ~200% scaling
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or FileFormatException)
        {
            return null;
        }
    }
}
