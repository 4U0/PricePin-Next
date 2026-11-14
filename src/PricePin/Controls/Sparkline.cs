using System.Windows;
using System.Windows.Media;

namespace PricePin.Controls;

/// <summary>
/// A tiny line chart of recent prices with a soft gradient fill under the line and a
/// dot on the latest value. Drawn directly (no layout children) so it stays cheap to
/// redraw on every price update.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(Sparkline),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        var w = ActualWidth;
        var h = ActualHeight;
        if (values is not { Count: > 1 } || w <= 0 || h <= 0)
            return;

        const double pad = 2.5; // room for the line thickness and end dot
        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        var stepX = (w - pad * 2) / (values.Count - 1);

        double Y(double v) => range <= 0 ? h / 2 : pad + (h - pad * 2) * (1 - (v - min) / range);

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            var first = new Point(pad, Y(values[0]));
            lc.BeginFigure(first, isFilled: false, isClosed: false);
            ac.BeginFigure(new Point(pad, h), isFilled: true, isClosed: true);
            ac.LineTo(first, false, false);
            for (var i = 1; i < values.Count; i++)
            {
                var p = new Point(pad + i * stepX, Y(values[i]));
                lc.LineTo(p, true, true);
                ac.LineTo(p, false, false);
            }
            ac.LineTo(new Point(pad + (values.Count - 1) * stepX, h), false, false);
        }
        line.Freeze();
        area.Freeze();

        var color = LineColor;
        var fill = new LinearGradientBrush(
            Color.FromArgb(70, color.R, color.G, color.B),
            Color.FromArgb(0, color.R, color.G, color.B),
            90);
        fill.Freeze();
        var stroke = new SolidColorBrush(color);
        stroke.Freeze();
        var pen = new Pen(stroke, 1.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, line);

        var last = new Point(pad + (values.Count - 1) * stepX, Y(values[^1]));
        var halo = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B));
        halo.Freeze();
        dc.DrawEllipse(halo, null, last, 4, 4);
        dc.DrawEllipse(stroke, null, last, 2, 2);
    }
}
