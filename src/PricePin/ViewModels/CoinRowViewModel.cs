using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PricePin.ViewModels;

/// <summary>One coin row in the widget.</summary>
public sealed partial class CoinRowViewModel(string symbol, string name, Color accent) : ObservableObject
{
    public string Symbol { get; } = symbol;
    public string Name { get; } = name;

    /// <summary>Colour of the round badge behind the symbol's first letter.</summary>
    public Brush AccentBrush { get; } = Freeze(new SolidColorBrush(accent));

    public string Initial => Symbol[..1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriceText))]
    private decimal _price;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeText), nameof(IsUp))]
    private decimal _changePercent24h;

    public bool IsUp => ChangePercent24h >= 0;

    public string PriceText => FormatPrice(Price);

    public string ChangeText =>
        (ChangePercent24h >= 0 ? "+" : "") + ChangePercent24h.ToString("0.00", CultureInfo.InvariantCulture) + "%";

    /// <summary>Shows enough decimals to be useful for both $90,000 coins and $0.0000123 coins.</summary>
    public static string FormatPrice(decimal price)
    {
        var abs = Math.Abs(price);
        var format = abs switch
        {
            >= 1000m => "#,0.00",
            >= 1m => "#,0.00##",
            >= 0.01m => "0.0000##",
            0m => "0.00",
            _ => "0.00000000",
        };
        return "$" + price.ToString(format, CultureInfo.InvariantCulture);
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
