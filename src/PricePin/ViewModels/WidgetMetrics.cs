using System.Windows;

namespace PricePin.ViewModels;

/// <summary>Layout numbers shared between XAML and code.</summary>
public static class WidgetMetrics
{
    /// <summary>Transparent space around the card that holds its drop shadow (device-independent units).</summary>
    public const double ShadowSize = 14;

    public static readonly Thickness ShadowMargin = new(ShadowSize);

    /// <summary>How close (in device-independent units) the card must get to a screen edge to snap to it.</summary>
    public const double SnapDistance = 14;

    /// <summary>Gap kept between the card and the screen edge when placed by default.</summary>
    public const double DefaultEdgeGap = 12;
}
