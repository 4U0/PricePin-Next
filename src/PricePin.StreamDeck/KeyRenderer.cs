using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;

namespace PricePin.StreamDeck;

/// <summary>What one key shows, chosen in the key's settings page.</summary>
/// <param name="Coin">Coin symbol, e.g. "BTC".</param>
/// <param name="Quote">Price currency: USD, USDT, EUR, TRY or GBP.</param>
/// <param name="Show">Bottom line: "change" (24h %) or "range" (24h low and high).</param>
internal sealed record KeySettings(string Coin, string Quote, string Show)
{
    public static readonly IReadOnlyList<string> Quotes = ["USD", "USDT", "EUR", "TRY", "GBP"];
    public static readonly KeySettings Default = new("BTC", "USD", "change");

    public bool ShowsRange => Show == "range";

    /// <summary>Reads the settings Stream Deck stored for a key; anything missing or odd falls back to the default.</summary>
    public static KeySettings From(JsonElement? settings)
    {
        if (settings is not { ValueKind: JsonValueKind.Object } s)
            return Default;
        var coin = Text(s, "coin")?.Trim().ToUpperInvariant() ?? "";
        if (coin.Length is 0 or > 15 || !coin.All(char.IsLetterOrDigit))
            coin = Default.Coin;
        var quote = Text(s, "quote")?.Trim().ToUpperInvariant() ?? "";
        if (!Quotes.Contains(quote))
            quote = Default.Quote;
        var show = Text(s, "show") == "range" ? "range" : "change";
        return new KeySettings(coin, quote, show);
    }

    public Dictionary<string, string> ToPayload() => new() { ["coin"] = Coin, ["quote"] = Quote, ["show"] = Show };

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Everything drawn on one key.</summary>
/// <param name="Bottom">"+2.41%" or "96.1k – 98.4k".</param>
/// <param name="Up">Colour of the bottom line and sparkline: true green, false red, null grey.</param>
/// <param name="Spark">Recent closing prices, oldest first; may be empty.</param>
/// <param name="Status">Shown instead of the price while there is none yet ("…", "offline").</param>
internal sealed record KeyView(string Symbol, string? Price, string Bottom, bool? Up, IReadOnlyList<decimal> Spark, string? Status);

/// <summary>Draws a key as a 144×144 SVG, which Stream Deck accepts as a data URL.</summary>
internal static class KeyRenderer
{
    public const int Size = 144;
    private const string Background = "#0E1116";
    private const string Green = "#2EBD85";
    private const string Red = "#F6465D";
    private const string Grey = "#8B93A1";
    private const string Font = "Segoe UI, Arial, sans-serif";

    public static string Svg(KeyView v)
    {
        var color = v.Up switch { true => Green, false => Red, null => Grey };
        var sb = new StringBuilder(1024);
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Size}\" height=\"{Size}\" viewBox=\"0 0 {Size} {Size}\">");
        sb.Append($"<rect width=\"{Size}\" height=\"{Size}\" fill=\"{Background}\"/>");

        if (Sparkline(v.Spark, 10, 78, 124, 34) is { } points)
            sb.Append($"<polyline points=\"{points}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"3\" stroke-linejoin=\"round\" stroke-linecap=\"round\" opacity=\"0.35\"/>");

        sb.Append(Text(72, 36, 26, 700, "#FFFFFF", v.Symbol));
        var main = v.Price ?? v.Status ?? "…";
        sb.Append(Text(72, 80, PriceFontSize(main), 600, v.Price is null ? Grey : "#FFFFFF", main));
        if (v.Bottom.Length > 0)
            sb.Append(Text(72, 126, v.Bottom.Length > 11 ? 17 : 22, 600, color, v.Bottom));
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>The form Stream Deck's setImage takes: a URL-encoded SVG data URL.</summary>
    public static string DataUrl(string svg) => "data:image/svg+xml," + Uri.EscapeDataString(svg);

    /// <summary>Smaller text for longer prices so "$103,456" and "0.00001234" both fit.</summary>
    internal static int PriceFontSize(string text) => text.Length switch
    {
        <= 6 => 32,
        <= 8 => 28,
        <= 10 => 23,
        _ => 19,
    };

    private static string Text(int x, int y, int size, int weight, string fill, string text) =>
        $"<text x=\"{x}\" y=\"{y}\" font-family=\"{Font}\" font-size=\"{size}\" font-weight=\"{weight}\" fill=\"{fill}\" text-anchor=\"middle\">{SecurityElement.Escape(text)}</text>";

    /// <summary>Polyline points scaled into a box, or null when there are too few prices.</summary>
    internal static string? Sparkline(IReadOnlyList<decimal> values, int left, int top, int width, int height)
    {
        if (values.Count < 2)
            return null;
        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        var sb = new StringBuilder();
        for (var i = 0; i < values.Count; i++)
        {
            var x = left + (double)i / (values.Count - 1) * width;
            var y = range == 0 ? top + height / 2.0 : top + height - (double)((values[i] - min) / range) * height;
            if (i > 0)
                sb.Append(' ');
            sb.Append(x.ToString("0.#", CultureInfo.InvariantCulture)).Append(',').Append(y.ToString("0.#", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    // ---- Number text (always the same style, whatever the PC's language) ----------------

    public static string CurrencySign(string quote) => quote switch
    {
        "USD" or "USDT" => "$",
        "EUR" => "€",
        "GBP" => "£",
        "TRY" => "₺",
        _ => "",
    };

    /// <summary>"$97,123", "$3,412.5", "$1.234", "$0.0001234".</summary>
    public static string Price(decimal price, string quote)
    {
        var c = CultureInfo.InvariantCulture;
        var text = price switch
        {
            >= 10_000 => price.ToString("#,0", c),
            >= 1_000 => price.ToString("#,0.#", c),
            >= 1 => price.ToString("0.###", c),
            > 0 => price.ToString("0.########", c) is var s && s.Length > 10 ? s[..10] : price.ToString("0.########", c),
            _ => "0",
        };
        return CurrencySign(quote) + text;
    }

    /// <summary>"+2.41%" or "-0.8%".</summary>
    public static string Change(decimal percent) =>
        (percent > 0 ? "+" : "") + percent.ToString(Math.Abs(percent) >= 10 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + "%";

    /// <summary>"96.1k–98.4k": the 24h low and high, short.</summary>
    public static string Range(decimal low, decimal high) => Short(low) + "–" + Short(high);

    internal static string Short(decimal v)
    {
        var c = CultureInfo.InvariantCulture;
        return v switch
        {
            >= 1_000_000 => (v / 1_000_000m).ToString("0.##", c) + "M",
            >= 10_000 => (v / 1_000m).ToString("0.#", c) + "k",
            >= 1_000 => (v / 1_000m).ToString("0.##", c) + "k",
            >= 1 => v.ToString("0.##", c),
            _ => v.ToString("0.####", c),
        };
    }
}
