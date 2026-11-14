using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PricePin.ViewModels;

namespace PricePin.Controls;

/// <summary>
/// Briefly tints a row green or red when its price moves.
/// Bind <c>Flash.Pulse</c> on a Border (background flash) or a TextBlock (text flash)
/// to <see cref="CoinRowViewModel.FlashPulse"/>; each change starts one flash.
/// </summary>
public static class Flash
{
    private static readonly Color Up = Color.FromRgb(0x22, 0xD3, 0x99);
    private static readonly Color Down = Color.FromRgb(0xF4, 0x3F, 0x5E);
    private static readonly Duration BackgroundDuration = TimeSpan.FromMilliseconds(900);
    private static readonly Duration TextDuration = TimeSpan.FromMilliseconds(600);

    public static readonly DependencyProperty PulseProperty = DependencyProperty.RegisterAttached(
        "Pulse", typeof(int), typeof(Flash), new PropertyMetadata(0, OnPulseChanged));

    /// <summary>Colour the text returns to after a flash.</summary>
    public static readonly DependencyProperty RestingColorProperty = DependencyProperty.RegisterAttached(
        "RestingColor", typeof(Color), typeof(Flash), new PropertyMetadata(Color.FromRgb(0xF1, 0xF5, 0xF9)));

    public static int GetPulse(DependencyObject d) => (int)d.GetValue(PulseProperty);
    public static void SetPulse(DependencyObject d, int value) => d.SetValue(PulseProperty, value);

    public static Color GetRestingColor(DependencyObject d) => (Color)d.GetValue(RestingColorProperty);
    public static void SetRestingColor(DependencyObject d, Color value) => d.SetValue(RestingColorProperty, value);

    private static void OnPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement { DataContext: CoinRowViewModel row } element || !element.IsLoaded)
            return;

        var color = Lookup(element, row.LastMoveUp ? "UpColor" : "DownColor", row.LastMoveUp ? Up : Down);
        switch (element)
        {
            case Border border:
                border.Background = Animate(Color.FromArgb(46, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), BackgroundDuration);
                break;
            case TextBlock text:
                text.Foreground = Animate(color, Lookup(text, "TextPrimaryColor", GetRestingColor(text)), TextDuration);
                break;
        }
    }

    /// <summary>Theme colour if the current theme defines it, so flashes match light and dark themes.</summary>
    private static Color Lookup(FrameworkElement element, string key, Color fallback) =>
        element.TryFindResource(key) is Color c ? c : fallback;

    private static SolidColorBrush Animate(Color from, Color to, Duration duration)
    {
        var brush = new SolidColorBrush(from);
        var animation = new ColorAnimation(from, to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        // The widget is a layered window, so every frame costs a full redraw; 30 fps is plenty for a fade.
        Timeline.SetDesiredFrameRate(animation, 30);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        return brush;
    }
}
