using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PricePin.Core.History;

namespace PricePin.Controls;

/// <summary>
/// The coin detail chart: closing prices as a line with a soft fill, a few price gridlines,
/// start and end times, and a crosshair that shows the time and price under the mouse.
/// </summary>
public sealed class PriceChart : FrameworkElement
{
    public static readonly DependencyProperty CandlesProperty = DependencyProperty.Register(
        nameof(Candles), typeof(IReadOnlyList<Candle>), typeof(PriceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(PriceChart),
        new FrameworkPropertyMetadata(Colors.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(PriceChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(PriceChart),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(PriceChart),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PopupBrushProperty = DependencyProperty.Register(
        nameof(PopupBrush), typeof(Brush), typeof(PriceChart),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Formats a price for the labels, e.g. "$97,123.45".</summary>
    public static readonly DependencyProperty PriceFormatterProperty = DependencyProperty.Register(
        nameof(PriceFormatter), typeof(Func<decimal, string>), typeof(PriceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>True for day charts: label times as hours rather than dates.</summary>
    public static readonly DependencyProperty ShowTimeOfDayProperty = DependencyProperty.Register(
        nameof(ShowTimeOfDay), typeof(bool), typeof(PriceChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double RightAxis = 74;
    private const double BottomAxis = 20;
    private const double TopPad = 8;
    private Point? _mouse;

    public PriceChart()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;
    }

    public IReadOnlyList<Candle>? Candles { get => (IReadOnlyList<Candle>?)GetValue(CandlesProperty); set => SetValue(CandlesProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush PopupBrush { get => (Brush)GetValue(PopupBrushProperty); set => SetValue(PopupBrushProperty, value); }
    public Func<decimal, string>? PriceFormatter { get => (Func<decimal, string>?)GetValue(PriceFormatterProperty); set => SetValue(PriceFormatterProperty, value); }
    public bool ShowTimeOfDay { get => (bool)GetValue(ShowTimeOfDayProperty); set => SetValue(ShowTimeOfDayProperty, value); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _mouse = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent background so the whole area receives mouse moves.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var candles = Candles;
        var plotW = ActualWidth - RightAxis;
        var plotH = ActualHeight - BottomAxis - TopPad;
        if (candles is not { Count: > 1 } || plotW <= 20 || plotH <= 20)
            return;

        var format = PriceFormatter ?? (p => p.ToString("N2", CultureInfo.CurrentCulture));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText Text(string s, Brush b, double size = 10.5) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, b, dpi);

        var lows = candles.Select(c => (double)c.Close).ToArray();
        var min = lows.Min();
        var max = lows.Max();
        if (max - min < max * 1e-9)
        {
            min -= Math.Max(Math.Abs(min) * 0.01, 1e-9);
            max += Math.Max(Math.Abs(max) * 0.01, 1e-9);
        }
        var pad = (max - min) * 0.08;
        min -= pad;
        max += pad;

        double X(int i) => i * plotW / (candles.Count - 1);
        double Y(double v) => TopPad + plotH * (1 - (v - min) / (max - min));

        // Gridlines with price labels.
        var gridPen = new Pen(GridBrush, 1) { DashStyle = new DashStyle([2, 4], 0) };
        gridPen.Freeze();
        for (var g = 0; g <= 3; g++)
        {
            var v = min + (max - min) * (g + 0.5) / 4;
            var y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(gridPen, new Point(0, y), new Point(plotW, y));
            var label = Text(format((decimal)v), LabelBrush);
            dc.DrawText(label, new Point(plotW + 8, y - label.Height / 2));
        }

        // Start and end times.
        string TimeLabel(DateTimeOffset t) => t.ToLocalTime().ToString(ShowTimeOfDay ? "t" : "d MMM", CultureInfo.CurrentCulture);
        var start = Text(TimeLabel(candles[0].Time), LabelBrush);
        var end = Text(TimeLabel(candles[^1].Time), LabelBrush);
        dc.DrawText(start, new Point(0, ActualHeight - BottomAxis + 4));
        dc.DrawText(end, new Point(plotW - end.Width, ActualHeight - BottomAxis + 4));

        // Line and fill.
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            var first = new Point(0, Y(lows[0]));
            lc.BeginFigure(first, false, false);
            ac.BeginFigure(new Point(0, TopPad + plotH), true, true);
            ac.LineTo(first, false, false);
            for (var i = 1; i < lows.Length; i++)
            {
                var p = new Point(X(i), Y(lows[i]));
                lc.LineTo(p, true, true);
                ac.LineTo(p, false, false);
            }
            ac.LineTo(new Point(X(lows.Length - 1), TopPad + plotH), false, false);
        }
        line.Freeze();
        area.Freeze();

        var color = LineColor;
        var fill = new LinearGradientBrush(Color.FromArgb(80, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        fill.Freeze();
        var stroke = new SolidColorBrush(color);
        stroke.Freeze();
        var pen = new Pen(stroke, 1.8) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, line);

        // Crosshair.
        if (_mouse is { } m && m.X >= 0 && m.X <= plotW)
        {
            var i = (int)Math.Round(m.X / plotW * (candles.Count - 1));
            i = Math.Clamp(i, 0, candles.Count - 1);
            var c = candles[i];
            var px = X(i);
            var py = Y((double)c.Close);

            var crossPen = new Pen(LabelBrush, 1);
            crossPen.Freeze();
            dc.DrawLine(crossPen, new Point(Math.Round(px) + 0.5, TopPad), new Point(Math.Round(px) + 0.5, TopPad + plotH));
            dc.DrawEllipse(stroke, new Pen(PopupBrush, 2), new Point(px, py), 4, 4);

            var when = c.Time.ToLocalTime().ToString(ShowTimeOfDay ? "g" : "D", CultureInfo.CurrentCulture);
            var price = Text(format(c.Close), TextBrush, 12.5);
            var time = Text(when, LabelBrush, 10.5);
            var boxW = Math.Max(price.Width, time.Width) + 16;
            var boxH = price.Height + time.Height + 10;
            var boxX = px + 12 + boxW > plotW ? px - 12 - boxW : px + 12;
            var boxY = Math.Clamp(py - boxH - 8, TopPad, TopPad + plotH - boxH);
            dc.DrawRoundedRectangle(PopupBrush, new Pen(GridBrush, 1), new Rect(boxX, boxY, boxW, boxH), 6, 6);
            dc.DrawText(price, new Point(boxX + 8, boxY + 4));
            dc.DrawText(time, new Point(boxX + 8, boxY + 6 + price.Height));
        }
    }
}
