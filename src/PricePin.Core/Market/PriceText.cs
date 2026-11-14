using System.Globalization;

namespace PricePin.Core.Market;

/// <summary>Price text helpers that don't depend on the UI.</summary>
public static class PriceText
{
    /// <summary>
    /// At most four characters, for the tray icon: 1.2M, 123k, 64k, 3.2k, 152, 23.4, 2.41, .123.
    /// </summary>
    public static string Tiny(decimal price)
    {
        var v = Math.Abs(price);
        var inv = CultureInfo.InvariantCulture;
        string s = v switch
        {
            >= 999_500_000m => (v / 1_000_000_000m).ToString("0.#", inv) + "B",
            >= 9_950_000m => (v / 1_000_000m).ToString("0", inv) + "M",
            >= 999_500m => (v / 1_000_000m).ToString("0.#", inv) + "M",
            >= 9_950m => (v / 1_000m).ToString("0", inv) + "k",
            >= 999.5m => (v / 1_000m).ToString("0.0", inv) + "k",
            >= 99.95m => v.ToString("0", inv),
            >= 9.995m => v.ToString("0.0", inv),
            >= 1m => v.ToString("0.00", inv),
            >= 0.0005m => v.ToString(".000", inv),
            0m => "0",
            _ => "<.01",
        };
        return s;
    }

    /// <summary>
    /// Reads a price the way people type it: "70,000.50", "70.000,50", "70000,5", "$1,234", "0,00012".
    /// When a single separator could be either (e.g. "70,000"), the user's culture decides.
    /// </summary>
    public static bool TryParse(string? text, CultureInfo culture, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = new string(text.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        if (s.Length == 0 || s.Count(c => c == '-') > (s.StartsWith('-') ? 1 : 0))
            return false;

        var lastDot = s.LastIndexOf('.');
        var lastComma = s.LastIndexOf(',');
        char? decimalSep;
        if (lastDot >= 0 && lastComma >= 0)
            decimalSep = lastDot > lastComma ? '.' : ',';
        else if (lastDot < 0 && lastComma < 0)
            decimalSep = null;
        else
        {
            var sep = lastDot >= 0 ? '.' : ',';
            var count = s.Count(c => c == sep);
            var digitsAfter = s.Length - s.LastIndexOf(sep) - 1;
            if (count > 1)
                decimalSep = null;                       // 1.000.000 or 1,000,000: grouping
            else if (digitsAfter != 3)
                decimalSep = sep;                        // 70000,5 or 0.25
            else
            {
                // "70,000" / "70.000": ambiguous. Leading "0" can't be grouping ("0,123").
                var intPart = s[..s.LastIndexOf(sep)].TrimStart('-');
                decimalSep = intPart is "" or "0" || culture.NumberFormat.NumberDecimalSeparator == sep.ToString()
                    ? sep
                    : null;
            }
        }

        var normalized = decimalSep is { } d
            ? s.Replace(d == '.' ? "," : ".", "").Replace(d, '.')
            : s.Replace(",", "").Replace(".", "");
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }
}
