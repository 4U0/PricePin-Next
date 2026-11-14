using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PricePin.Services;

/// <summary>Draws a short price ("97k") as a tray icon, sized for the screen's DPI.</summary>
public static class TrayIconRenderer
{
    private static readonly Color Up = Color.FromRgb(0x22, 0xC5, 0x5E);
    private static readonly Color Down = Color.FromRgb(0xF8, 0x71, 0x71);
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static BitmapSource Render(string text, bool up)
    {
        var size = IconSize();
        var brush = new SolidColorBrush(up ? Up : Down);
        brush.Freeze();
        var outline = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0));
        outline.Freeze();

        // Largest font that fits the icon's width.
        var fontSize = size * 0.75;
        FormattedText ft;
        while (true)
        {
            ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, fontSize, brush, 1.0);
            if (ft.WidthIncludingTrailingWhitespace <= size || fontSize <= 5)
                break;
            fontSize -= 0.5;
        }

        var visual = new DrawingVisual();
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        using (var dc = visual.RenderOpen())
        {
            var origin = new Point((size - ft.WidthIncludingTrailingWhitespace) / 2, (size - ft.Height) / 2);
            // A thin dark outline keeps the digits readable on light and dark taskbars.
            var geometry = ft.BuildGeometry(origin);
            dc.DrawGeometry(null, new Pen(outline, Math.Max(1, size / 16.0)) { LineJoin = PenLineJoin.Round }, geometry);
            dc.DrawGeometry(brush, null, geometry);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Small-icon size in physical pixels: 16 at 100% scaling, 32 at 200%.</summary>
    private static int IconSize()
    {
        try
        {
            var dpi = GetDpiForSystem();
            var size = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
            return size > 0 ? size : 16;
        }
        catch (EntryPointNotFoundException)
        {
            return 16;
        }
    }

    private const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
