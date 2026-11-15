using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using PricePin.Core.History;
using PricePin.Core.Market;
using PricePin.Core.Settings;

namespace PricePin.StreamDeck;

/// <summary>
/// The plugin's logic, separate from the socket so it can be tested: it keeps the keys Stream Deck
/// shows, streams their coins (one live connection per currency), and redraws a key when what
/// it shows changes. Pressing a key switches its bottom line between the 24h change and the 24h range.
/// </summary>
internal sealed class PluginHost : IAsyncDisposable
{
    public const string ActionUuid = "com.pricepin.streamdeck.price";
    public static readonly TimeSpan SparkEvery = TimeSpan.FromMinutes(15);

    private readonly Func<string, Task> _send;
    private readonly bool _live;
    private readonly Dictionary<string, KeySettings> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _drawn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MarketDataService> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _feedCoins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Quote, string Coin), Ticker> _prices = new();
    private readonly ConcurrentDictionary<(string Quote, string Coin), decimal[]> _sparks = new();
    private readonly ConcurrentDictionary<(string Quote, string Coin), DateTime> _sparkAt = new();
    private readonly ConcurrentDictionary<string, FeedState> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly CandleService _candles = new();
    private readonly CancellationTokenSource _lifetime = new();

    /// <param name="send">Sends one JSON message to Stream Deck.</param>
    /// <param name="live">False in tests: no network, prices come from <see cref="OnTicker"/>.</param>
    public PluginHost(Func<string, Task> send, bool live = true)
    {
        _send = send;
        _live = live;
    }

    public IReadOnlyDictionary<string, KeySettings> Keys => _keys;

    /// <summary>Handles one message from Stream Deck.</summary>
    public async Task HandleAsync(string json)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }
        if (Str(root, "event") is not { } ev || Str(root, "context") is not { } context)
            return;
        if (Str(root, "action") is { } action && action != ActionUuid)
            return;
        JsonElement? settings = root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("settings", out var s) ? s : null;

        switch (ev)
        {
            case "willAppear":
            case "didReceiveSettings":
                _keys[context] = KeySettings.From(settings);
                _drawn.Remove(context);
                UpdateFeeds();
                await DrawAsync(context).ConfigureAwait(false);
                break;
            case "willDisappear":
                _keys.Remove(context);
                _drawn.Remove(context);
                UpdateFeeds();
                break;
            case "keyDown" when _keys.TryGetValue(context, out var key):
                var next = key with { Show = key.ShowsRange ? "change" : "range" };
                _keys[context] = next;
                await SendAsync(new JsonObject
                {
                    ["event"] = "setSettings",
                    ["context"] = context,
                    ["payload"] = new JsonObject(next.ToPayload().Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value)))),
                }).ConfigureAwait(false);
                await DrawAsync(context).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>Called about once a second: redraws keys whose numbers changed and refreshes sparklines.</summary>
    public async Task TickAsync()
    {
        foreach (var context in _keys.Keys.ToList())
            await DrawAsync(context).ConfigureAwait(false);
        if (_live)
            RefreshSparks();
    }

    /// <summary>A price update from a feed (or a test).</summary>
    internal void OnTicker(string quote, Ticker t)
    {
        var key = (quote.ToUpperInvariant(), t.Base.ToUpperInvariant());
        _prices.AddOrUpdate(key, t, (_, old) => t with
        {
            Open24h = t.Open24h ?? old.Open24h,
            High24h = t.High24h ?? old.High24h,
            Low24h = t.Low24h ?? old.Low24h,
            ChangePercent24h = t.ChangePercent24h ?? old.ChangePercent24h,
        });
    }

    internal void SetSpark(string quote, string coin, decimal[] closes) => _sparks[(quote, coin)] = closes;

    /// <summary>What a key shows right now.</summary>
    internal KeyView ViewFor(KeySettings k)
    {
        var spark = _sparks.TryGetValue((k.Quote, k.Coin), out var sp) ? sp : [];
        if (!_prices.TryGetValue((k.Quote, k.Coin), out var t))
        {
            var offline = _status.TryGetValue(k.Quote, out var st) && st == FeedState.Offline;
            return new KeyView(k.Coin, null, k.Quote, null, spark, offline ? "offline" : "…");
        }

        decimal? change = t.ChangePercent24h
                          ?? (t.Open24h is { } open && open > 0 ? Math.Round((t.Last - open) / open * 100m, 2) : null);
        string bottom;
        if (k.ShowsRange && t.Low24h is { } low && t.High24h is { } high)
            bottom = KeyRenderer.Range(low, high);
        else
            bottom = change is { } c ? KeyRenderer.Change(c) : "";
        return new KeyView(k.Coin, KeyRenderer.Price(t.Last, k.Quote), bottom, change is { } ch ? ch >= 0 : null, spark, null);
    }

    private async Task DrawAsync(string context)
    {
        if (!_keys.TryGetValue(context, out var k))
            return;
        var image = KeyRenderer.DataUrl(KeyRenderer.Svg(ViewFor(k)));
        if (_drawn.TryGetValue(context, out var last) && last == image)
            return;
        _drawn[context] = image;
        await SendAsync(new JsonObject
        {
            ["event"] = "setImage",
            ["context"] = context,
            ["payload"] = new JsonObject { ["image"] = image, ["target"] = 0 },
        }).ConfigureAwait(false);
    }

    private Task SendAsync(JsonObject message) => _send(message.ToJsonString());

    // ---- Feeds ---------------------------------------------------------------------------

    /// <summary>One live connection per currency, with exactly the coins on the keys.</summary>
    private void UpdateFeeds()
    {
        var wanted = _keys.Values
            .GroupBy(k => k.Quote, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(k => k.Coin).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList(),
                StringComparer.OrdinalIgnoreCase);
        if (!_live)
            return;

        foreach (var quote in _feeds.Keys.Where(q => !wanted.ContainsKey(q)).ToList())
        {
            _ = _feeds[quote].DisposeAsync().AsTask();
            _feeds.Remove(quote);
            _feedCoins.Remove(quote);
        }
        foreach (var (quote, coins) in wanted)
        {
            var signature = string.Join(",", coins);
            if (_feedCoins.TryGetValue(quote, out var current) && current == signature)
                continue;
            if (!_feeds.TryGetValue(quote, out var feed))
            {
                feed = new MarketDataService();
                var q = quote;
                feed.TickReceived += t => OnTicker(q, t);
                feed.StatusChanged += st => _status[q] = st.State;
                _feeds[quote] = feed;
            }
            _feedCoins[quote] = signature;
            feed.Start(coins.Select(c => new MarketSymbol(c, quote)).ToList(), DataSource.Auto);
        }
    }

    /// <summary>The faint chart behind the price: the last 24 hours, refreshed every 15 minutes.</summary>
    private void RefreshSparks()
    {
        foreach (var k in _keys.Values.DistinctBy(k => (k.Quote, k.Coin)))
        {
            var key = (k.Quote, k.Coin);
            if (_sparkAt.TryGetValue(key, out var at) && DateTime.UtcNow - at < SparkEvery)
                continue;
            _sparkAt[key] = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (await _candles.GetAsync(new MarketSymbol(k.Coin, k.Quote), ChartRange.Day, _lifetime.Token).ConfigureAwait(false) is { } series)
                        _sparks[key] = series.Candles.Select(c => c.Close).ToArray();
                }
                catch (Exception)
                {
                    // No sparkline this time; the price still shows.
                }
            });
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        foreach (var feed in _feeds.Values)
            await feed.DisposeAsync().ConfigureAwait(false);
        _feeds.Clear();
    }
}
