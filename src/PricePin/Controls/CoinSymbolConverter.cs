using System.Globalization;
using System.Windows.Data;
using PricePin.Core.Coins;

namespace PricePin.Controls;

/// <summary>Shows a coin key as its symbol: "BTC" stays "BTC", a DEX token key becomes the token's symbol.</summary>
public sealed class CoinSymbolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? CoinCatalog.DisplaySymbol(key) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
