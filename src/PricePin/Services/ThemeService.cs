using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PricePin.Core.Settings;

namespace PricePin.Services;

/// <summary>
/// Applies the light or dark palette and the accent colour app-wide, and follows the
/// Windows app theme live when the user picks "System".
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly Uri DarkUri = new("Themes/Colors.Dark.xaml", UriKind.Relative);
    private static readonly Uri LightUri = new("Themes/Colors.Light.xaml", UriKind.Relative);

    private readonly ResourceDictionary _appResources;
    private AppTheme _mode;

    public ThemeService(ResourceDictionary appResources)
    {
        _appResources = appResources;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>True when the dark palette is currently showing.</summary>
    public bool IsDark { get; private set; } = true;

    public void Apply(AppTheme mode, string accentHex)
    {
        _mode = mode;
        ApplyPalette();
        ApplyAccent(accentHex);
    }

    public void ApplyAccent(string accentHex)
    {
        if (!AccentPresets.IsValid(accentHex))
            accentHex = AccentPresets.Default;
        var color = (Color)ColorConverter.ConvertFromString(accentHex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _appResources["AccentColor"] = color;
        _appResources["AccentBrush"] = brush;
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void ApplyPalette()
    {
        var dark = _mode switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => WindowsAppsUseDarkTheme(),
        };
        IsDark = dark;

        // The palette is always the first merged dictionary (see App.xaml).
        var dictionaries = _appResources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        if (dictionaries.Count > 0)
            dictionaries[0] = palette;
        else
            dictionaries.Add(palette);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode == AppTheme.System && e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            Application.Current?.Dispatcher.BeginInvoke(ApplyPalette);
    }

    private static bool WindowsAppsUseDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
