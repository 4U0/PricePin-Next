using System.Text.Json;
using System.Xml.Linq;
using PricePin.Core.Market;
using PricePin.StreamDeck;

namespace PricePin.Core.Tests;

public class StreamDeckTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Settings_FallBackToDefaults()
    {
        Assert.Equal(KeySettings.Default, KeySettings.From(null));
        Assert.Equal(new KeySettings("SOL", "EUR", "range"), KeySettings.From(Parse("""{"coin":" sol ","quote":"eur","show":"range"}""")));
        Assert.Equal(KeySettings.Default, KeySettings.From(Parse("""{"coin":"<script>","quote":"JPY","show":"x"}""")));
        Assert.Equal("BTC", KeySettings.From(Parse("""{"coin":""}""")).Coin);
    }

    [Theory]
    [InlineData(97123.45, "USD", "$97,123")]
    [InlineData(3412.56, "EUR", "€3,412.6")]
    [InlineData(1.23456, "GBP", "£1.235")]
    [InlineData(0.00001234, "TRY", "₺0.00001234")]
    [InlineData(0, "USDT", "$0")]
    public void Prices_AreShortAndCultureFree(decimal price, string quote, string expected) =>
        Assert.Equal(expected, KeyRenderer.Price(price, quote));

    [Fact]
    public void ChangeAndRange_Text()
    {
        Assert.Equal("+2.41%", KeyRenderer.Change(2.41m));
        Assert.Equal("-0.80%", KeyRenderer.Change(-0.8m));
        Assert.Equal("+12.3%", KeyRenderer.Change(12.34m));
        Assert.Equal("96.1k–98.4k", KeyRenderer.Range(96_100m, 98_400m));
        Assert.Equal("0.5–0.62", KeyRenderer.Range(0.5m, 0.62m));
    }

    [Fact]
    public void Svg_IsValidXml_WithEscapedText_AndEncodedForStreamDeck()
    {
        var svg = KeyRenderer.Svg(new KeyView("A&B", "$1<2", "+1.00%", true, [1m, 2m, 1.5m], null));
        var xml = XDocument.Parse(svg);
        Assert.Equal("svg", xml.Root!.Name.LocalName);
        Assert.Contains("A&amp;B", svg);
        Assert.Contains("<polyline", svg);

        var url = KeyRenderer.DataUrl(svg);
        Assert.StartsWith("data:image/svg+xml,", url);
        Assert.DoesNotContain("#", url); // a raw # would end the data URL
        Assert.DoesNotContain("<", url);
    }

    [Fact]
    public void Sparkline_NeedsTwoPoints_AndHandlesFlatPrices()
    {
        Assert.Null(KeyRenderer.Sparkline([1m], 0, 0, 100, 10));
        Assert.Equal("0,5 100,5", KeyRenderer.Sparkline([3m, 3m], 0, 0, 100, 10));
        Assert.Equal("0,10 100,0", KeyRenderer.Sparkline([1m, 2m], 0, 0, 100, 10));
    }

    [Fact]
    public async Task Host_DrawsKeys_RedrawsOnlyOnChange_AndTogglesOnPress()
    {
        var sent = new List<JsonElement>();
        await using var host = new PluginHost(json =>
        {
            sent.Add(Parse(json));
            return Task.CompletedTask;
        }, live: false);

        await host.HandleAsync("""{"action":"com.pricepin.streamdeck.price","event":"willAppear","context":"k1","payload":{"settings":{"coin":"eth","quote":"USD"}}}""");
        Assert.Single(sent);
        Assert.Equal("setImage", sent[0].GetProperty("event").GetString());
        Assert.Equal("k1", sent[0].GetProperty("context").GetString());
        Assert.Equal("ETH", host.Keys["k1"].Coin);

        host.OnTicker("USD", new Ticker { Base = "ETH", Source = "Test", Last = 3500m, Open24h = 3400m, High24h = 3550m, Low24h = 3390m });
        await host.TickAsync();
        Assert.Equal(2, sent.Count);
        var image = Uri.UnescapeDataString(sent[1].GetProperty("payload").GetProperty("image").GetString()!);
        Assert.Contains("$3,500", image);
        Assert.Contains("+2.94%", image);

        await host.TickAsync(); // nothing changed: nothing sent
        Assert.Equal(2, sent.Count);

        await host.HandleAsync("""{"action":"com.pricepin.streamdeck.price","event":"keyDown","context":"k1","payload":{"settings":{}}}""");
        Assert.Equal("setSettings", sent[2].GetProperty("event").GetString());
        Assert.Equal("range", sent[2].GetProperty("payload").GetProperty("show").GetString());
        var ranged = Uri.UnescapeDataString(sent[3].GetProperty("payload").GetProperty("image").GetString()!);
        Assert.Contains("3.39k–3.55k", ranged);

        await host.HandleAsync("""{"action":"com.pricepin.streamdeck.price","event":"willDisappear","context":"k1"}""");
        Assert.Empty(host.Keys);
    }

    [Fact]
    public async Task Host_IgnoresOtherActionsAndJunk()
    {
        var count = 0;
        await using var host = new PluginHost(_ => { count++; return Task.CompletedTask; }, live: false);
        await host.HandleAsync("not json");
        await host.HandleAsync("""{"event":"willAppear"}""");
        await host.HandleAsync("""{"action":"com.other.plugin","event":"willAppear","context":"x"}""");
        Assert.Equal(0, count);
        Assert.Empty(host.Keys);
    }

    [Fact]
    public void Args_AreReadInPairs()
    {
        var map = PricePin.StreamDeck.Program.ParseArgs(["-port", "28196", "-pluginUUID", "abc", "-registerEvent", "registerPlugin", "-info", "{}"]);
        Assert.Equal("28196", map["port"]);
        Assert.Equal("abc", map["pluginUUID"]);
        Assert.Equal("{}", map["info"]);
    }
}
