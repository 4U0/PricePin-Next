using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PricePin.Controls;

/// <summary>A half-circle gauge from extreme fear (red, left) to extreme greed (green, right).</summary>
public sealed class FearGreedGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int?), typeof(FearGreedGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(FearGreedGauge),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(FearGreedGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Color[] Bands =
    [
        Color.FromRgb(0xEF, 0x44, 0x44), // extreme fear
        Color.FromRgb(0xF9, 0x73, 0x16), // fear
        Color.FromRgb(0xEA, 0xB3, 0x08), // neutral
        Color.FromRgb(0x84, 0xCC, 0x16), // greed
        Color.FromRgb(0x22, 0xC5, 0x5E), // extreme greed
    ];

    // Band edges matching MarketOverview.FearGreedLevel.
    private static readonly int[] Edges = [0, 25, 45, 56, 76, 100];

    public int? Value { get => (int?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 200 : available.Width, 0);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        const double thickness = 12;
        var radius = Math.Min(w / 2, h - 4) - thickness / 2;
        if (radius <= 10)
            return;
        var center = new Point(w / 2, h - 4);

        Point At(double value, double r)
        {
            var angle = Math.PI * (1 - value / 100.0); // 0 → left, 100 → right
            return new Point(center.X + r * Math.Cos(angle), center.Y - r * Math.Sin(angle));
        }

        for (var i = 0; i < Bands.Length; i++)
        {
            var from = Edges[i] + (i == 0 ? 0 : 0.8);
            var to = Edges[i + 1] - (i == Bands.Length - 1 ? 0 : 0.8);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(At(from, radius), false, false);
                g.ArcTo(At(to, radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
            }
            geometry.Freeze();
            var active = Value is { } v && v >= Edges[i] && (v < Edges[i + 1] || i == Bands.Length - 1);
            var color = Bands[i];
            var brush = new SolidColorBrush(Value is null || active ? color : Color.FromArgb(0x70, color.R, color.G, color.B));
            brush.Freeze();
            dc.DrawGeometry(null, new Pen(brush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, geometry);
        }

        if (Value is not { } value)
            return;

        // Needle and number.
        var tip = At(value, radius - thickness);
        var needle = new Pen(TextBrush, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        needle.Freeze();
        dc.DrawLine(needle, center, tip);
        dc.DrawEllipse(TextBrush, null, center, 5, 5);

        var text = new FormattedText(value.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            Math.Max(14, radius * 0.38), TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var y = center.Y - radius * 0.55 - text.Height / 2;
        // Keep the number clear of the needle when it points roughly straight up.
        var x = value is > 35 and < 65 ? center.X + radius * 0.32 : center.X - text.Width / 2;
        dc.DrawText(text, new Point(x, y));
    }
}
