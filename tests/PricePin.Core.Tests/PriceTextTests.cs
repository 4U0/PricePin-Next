using System.Globalization;
using PricePin.Core.Market;

namespace PricePin.Core.Tests;

public class PriceTextTests
{
    [Theory]
    [InlineData("97234.5", "97k")]
    [InlineData("123456", "123k")]
    [InlineData("3245.1", "3.2k")]
    [InlineData("999.6", "1.0k")]
    [InlineData("152.4", "152")]
    [InlineData("23.44", "23.4")]
    [InlineData("2.414", "2.41")]
    [InlineData("0.12345", ".123")]
    [InlineData("0.00001", "<.01")]
    [InlineData("1250000", "1.3M")]
    [InlineData("25000000", "25M")]
    public void Tiny_fits_in_four_characters(string price, string expected)
    {
        var text = PriceText.Tiny(decimal.Parse(price, CultureInfo.InvariantCulture));
        Assert.Equal(expected, text);
        Assert.True(text.Length <= 4, text);
    }

    [Theory]
    [InlineData("70,000.50", "en-US", "70000.50")]
    [InlineData("70.000,50", "tr-TR", "70000.50")]
    [InlineData("70000,5", "en-US", "70000.5")]
    [InlineData("$1,234", "en-US", "1234")]
    [InlineData("1,234", "tr-TR", "1.234")]
    [InlineData("1.234", "tr-TR", "1234")]
    [InlineData("1.234", "en-US", "1.234")]
    [InlineData("0,123", "en-US", "0.123")]
    [InlineData("1,000,000", "en-US", "1000000")]
    [InlineData(" 5 % ", "en-US", "5")]
    [InlineData("0.00001234", "tr-TR", "0.00001234")]
    public void TryParse_reads_what_people_type(string text, string culture, string expected)
    {
        Assert.True(PriceText.TryParse(text, CultureInfo.GetCultureInfo(culture), out var value));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1-2")]
    public void TryParse_rejects_junk(string text) =>
        Assert.False(PriceText.TryParse(text, CultureInfo.InvariantCulture, out _));
}
