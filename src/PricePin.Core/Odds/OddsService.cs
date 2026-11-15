using System.Globalization;
using System.Text.Json;
using PricePin.Core.Feeds;
using PricePin.Core.News;

namespace PricePin.Core.Odds;

/// <summary>One answer in a market: "Yes", "↑ $120,000", "December".</summary>
/// <param name="Chance">What traders are paying for it, 0 to 1, which reads as the chance they give it.</param>
/// <param name="Change24h">How much the chance moved in the last day, in the same 0 to 1 units.</param>
public sealed record OddsOutcome(string Label, decimal Chance, decimal? Change24h);

/// <summary>A prediction market question about crypto, with its current odds.</summary>
/// <param name="Exclusive">True when exactly one outcome can win (the chances add up to about 100%).</param>
/// <param name="Volume24hUsd">Money traded in the last day, in US dollars (Kalshi's is estimated from contracts).</param>
public sealed record OddsMarket(
    string Id,
    string Title,
    string Url,
    string Source,
    DateTimeOffset? Ends,
    decimal Volume24hUsd,
    decimal VolumeUsd,
    IReadOnlyList<OddsOutcome> Outcomes,
    bool Exclusive,
    IReadOnlyList<string> Coins)
{
    /// <summary>A plain yes-or-no question, shown as one big percentage.</summary>
    public bool IsYesNo => Outcomes is [{ Label: "Yes" }];

    /// <summary>
    /// The outcomes worth showing in a short list. When only one can win, the favourites. When each is its own
    /// yes-or-no (price ladders like "above $100k", "above $110k"), the ones closest to a coin flip,
    /// because the near-certain and near-impossible rungs say little. Either way they keep the market's order.
    /// </summary>
    public IReadOnlyList<OddsOutcome> Highlights(int count)
    {
        if (Outcomes.Count <= count)
            return Outcomes;
        var picked = Exclusive
            ? Outcomes.OrderByDescending(o => o.Chance).Take(count)
            : Outcomes.OrderBy(o => Math.Abs(o.Chance - 0.5m)).Take(count);
        var set = picked.ToHashSet();
        return Outcomes.Where(set.Contains).ToList();
    }

    /// <summary>The outcome a one-line summary leads with.</summary>
    public OddsOutcome Lead => IsYesNo ? Outcomes[0] : Highlights(1)[0];
}

/// <summary>Everything the odds tab shows. <see cref="Errors"/> lists sources that did not answer.</summary>
public sealed record OddsBoard(IReadOnlyList<OddsMarket> Markets, DateTimeOffset Time, IReadOnlyList<string> Errors);

/// <summary>
/// Crypto questions from the two biggest prediction markets, read from their public APIs with no account or key:
/// <list type="bullet">
/// <item>Polymarket's crypto events ("What price will Bitcoin hit in December?", "ETH ETF staking approved?").</item>
/// <item>Kalshi's Bitcoin and Ethereum price markets, which also keep working where Polymarket is blocked.</item>
/// </list>
/// Very short "up or down in the next 15 minutes" markets are left out; they are coin flips, not opinions.
/// </summary>
public sealed class OddsService
{
    public const string PolymarketUrl =
        "https://gamma-api.polymarket.com/events?tag_slug=crypto&active=true&closed=false&archived=false&order=volume24hr&ascending=false&limit=100";
    public const string KalshiBase = "https://api.elections.kalshi.com/trade-api/v2";

    /// <summary>Kalshi series to read: yearly highs and lows, and today's price ladders, for Bitcoin and Ethereum.</summary>
    public static readonly IReadOnlyList<string> KalshiSeries = ["KXBTCMAXY", "KXBTCMINY", "KXETHMAXY", "KXETHMINY", "KXBTCD", "KXETHD"];

    /// <summary>Markets that close sooner than this are left out: there is nothing left to read into them.</summary>
    public static readonly TimeSpan MinTimeLeft = TimeSpan.FromHours(1);

    public const int MaxMarkets = 60;

    public async Task<OddsBoard> FetchAsync(IReadOnlyList<NewsCoin> coins, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var polyTask = FetchPolymarketWithErrorAsync(now, ct);
        var kalshiTask = FetchKalshiWithErrorAsync(now, ct);
        await Task.WhenAll(polyTask, kalshiTask).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var markets = new List<OddsMarket>();
        if (polyTask.Result.Markets is { } poly)
            markets.AddRange(poly);
        else
            errors.Add("Polymarket");
        if (kalshiTask.Result.Markets is { } kalshi)
            markets.AddRange(kalshi);
        else
            errors.Add("Kalshi");
        return new OddsBoard(Merge(markets, coins), now, errors);
    }

    /// <summary>Busiest first, coins tagged from the title.</summary>
    internal static IReadOnlyList<OddsMarket> Merge(IEnumerable<OddsMarket> markets, IReadOnlyList<NewsCoin> coins) =>
        markets
            .DistinctBy(m => m.Id)
            .OrderByDescending(m => m.Volume24hUsd)
            .ThenByDescending(m => m.VolumeUsd)
            .Take(MaxMarkets)
            .Select(m => m with { Coins = NewsService.Tag(m.Title, coins) })
            .ToList();

    // ---- Polymarket ------------------------------------------------------------------

    internal static async Task<(IReadOnlyList<OddsMarket>? Markets, string? Error)> FetchPolymarketWithErrorAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var text = await Http.Client.GetStringAsync(PolymarketUrl, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var markets = ParsePolymarket(doc.RootElement, now);
            return (markets, markets is null ? "unexpected answer" : null);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // [{"id":"123","slug":"what-price-will-bitcoin-hit-in-december","title":"What price will Bitcoin hit in December?",
    //   "endDate":"2026-12-31T05:00:00Z","volume":1234567.8,"volume24hr":45678.9,"negRisk":false,
    //   "markets":[{"question":"Will Bitcoin reach $120,000 in December?","groupItemTitle":"↑ 120,000",
    //               "outcomes":"[\"Yes\", \"No\"]","outcomePrices":"[\"0.34\", \"0.66\"]","oneDayPriceChange":0.05,
    //               "active":true,"closed":false}]}]
    internal static IReadOnlyList<OddsMarket>? ParsePolymarket(JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<OddsMarket>();
        foreach (var e in root.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object
                || Json.String(e, "title")?.Trim() is not { Length: > 0 } title
                || Json.String(e, "slug") is not { Length: > 0 } slug
                || !slug.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                || IsShortTerm(title)
                || Bool(e, "closed") == true
                || !e.TryGetProperty("markets", out var marketsJson) || marketsJson.ValueKind != JsonValueKind.Array)
                continue;
            var ends = Json.Iso(e, "endDate");
            if (ends is { } end && end - now < MinTimeLeft)
                continue;

            var open = marketsJson.EnumerateArray()
                .Where(m => m.ValueKind == JsonValueKind.Object && Bool(m, "closed") != true && Bool(m, "active") != false)
                .ToList();
            var outcomes = new List<OddsOutcome>();
            var exclusive = Bool(e, "negRisk") == true;
            if (open.Count == 1 && StringArray(open[0], "outcomes") is { } names && names.Count >= 2 && names[0] != "Yes"
                && StringArray(open[0], "outcomePrices") is { } prices && prices.Count == names.Count)
            {
                // One market with named sides ("Up" / "Down", "Above" / "Below"): each side is an outcome.
                exclusive = true;
                for (var i = 0; i < names.Count; i++)
                {
                    if (Number(prices[i]) is { } p)
                        outcomes.Add(new OddsOutcome(names[i], p, i == 0 ? Json.Decimal(open[0], "oneDayPriceChange") : null));
                }
            }
            else
            {
                foreach (var m in open)
                {
                    if (YesChance(m) is not { } chance)
                        continue;
                    var label = open.Count == 1 ? "Yes" : Json.String(m, "groupItemTitle")?.Trim() is { Length: > 0 } g ? g : Json.String(m, "question")?.Trim();
                    if (string.IsNullOrEmpty(label))
                        continue;
                    outcomes.Add(new OddsOutcome(label, chance, Json.Decimal(m, "oneDayPriceChange")));
                }
            }
            if (outcomes.Count == 0)
                continue;
            list.Add(new OddsMarket(
                Id: "pm:" + (Json.String(e, "id") ?? slug),
                Title: title,
                Url: "https://polymarket.com/event/" + slug,
                Source: "Polymarket",
                Ends: ends,
                Volume24hUsd: Math.Max(0, Json.Decimal(e, "volume24hr") ?? 0),
                VolumeUsd: Math.Max(0, Json.Decimal(e, "volume") ?? 0),
                Outcomes: outcomes,
                Exclusive: exclusive,
                Coins: []));
        }
        return list;
    }

    /// <summary>"Bitcoin Up or Down - November 16, 3PM ET" and the 5/15-minute versions.</summary>
    internal static bool IsShortTerm(string title) =>
        title.Contains("Up or Down", StringComparison.OrdinalIgnoreCase);

    /// <summary>The "Yes" price of a yes-or-no market.</summary>
    private static decimal? YesChance(JsonElement m)
    {
        if (StringArray(m, "outcomePrices") is not { Count: >= 1 } prices || Number(prices[0]) is not { } p)
            return null;
        if (StringArray(m, "outcomes") is { Count: >= 1 } names && names[0] != "Yes")
            return null;
        return p;
    }

    /// <summary>Polymarket sends lists as JSON inside a string: "[\"Yes\", \"No\"]".</summary>
    private static List<string>? StringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v))
            return null;
        try
        {
            using var doc = v.ValueKind == JsonValueKind.String ? JsonDocument.Parse(v.GetString()!) : null;
            var array = doc?.RootElement ?? v;
            if (array.ValueKind != JsonValueKind.Array)
                return null;
            return array.EnumerateArray().Select(x => x.ValueKind switch
            {
                JsonValueKind.String => x.GetString()!,
                JsonValueKind.Number => x.GetRawText(),
                _ => "",
            }).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? Number(string s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d is >= 0 and <= 1 ? d : null;

    private static bool? Bool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    // ---- Kalshi ----------------------------------------------------------------------

    public static string KalshiEventsUrl(string series) =>
        $"{KalshiBase}/events?status=open&with_nested_markets=true&limit=20&series_ticker={Uri.EscapeDataString(series)}";

    internal static async Task<(IReadOnlyList<OddsMarket>? Markets, string? Error)> FetchKalshiWithErrorAsync(DateTimeOffset now, CancellationToken ct)
    {
        var tasks = KalshiSeries.Select(s => FetchKalshiSeriesWithErrorAsync(s, now, ct)).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        var answered = tasks.Where(t => t.Result.Market is not null || t.Result.Error is null).ToList();
        if (answered.Count == 0)
            return (null, tasks.Select(t => t.Result.Error).FirstOrDefault(e => e is not null) ?? "no answer");
        return (answered.Select(t => t.Result.Market).OfType<OddsMarket>().ToList(), null);
    }

    /// <summary>The soonest open event of one series, or null with no error when the series has none open.</summary>
    internal static async Task<(OddsMarket? Market, string? Error)> FetchKalshiSeriesWithErrorAsync(string series, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var text = await Http.Client.GetStringAsync(KalshiEventsUrl(series), ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            if (ParseKalshi(doc.RootElement, now) is not { } markets)
                return (null, "unexpected answer");
            return (markets.OrderBy(m => m.Ends ?? DateTimeOffset.MaxValue).FirstOrDefault(), null);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // {"events":[{"event_ticker":"KXBTCMAXY-26","series_ticker":"KXBTCMAXY","title":"How high will Bitcoin get this year?",
    //   "mutually_exclusive":false,"markets":[{"ticker":"KXBTCMAXY-26-T150000","yes_sub_title":"$150,000 or above",
    //   "status":"active","close_time":"2026-12-31T23:59:00Z","yes_bid_dollars":"0.1200","yes_ask_dollars":"0.1400",
    //   "last_price_dollars":"0.1300","previous_price_dollars":"0.1100","volume_24h_fp":"5230.00","volume_fp":"91230.00"}]}],"cursor":""}
    internal static IReadOnlyList<OddsMarket>? ParseKalshi(JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<OddsMarket>();
        foreach (var e in events.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object
                || Json.String(e, "title")?.Trim() is not { Length: > 0 } title
                || Json.String(e, "event_ticker") is not { Length: > 0 } ticker
                || Json.String(e, "series_ticker") is not { Length: > 0 } series
                || !series.All(char.IsAsciiLetterOrDigit)
                || IsShortTerm(title)
                || !e.TryGetProperty("markets", out var marketsJson) || marketsJson.ValueKind != JsonValueKind.Array)
                continue;

            var outcomes = new List<(OddsOutcome Outcome, decimal Strike)>();
            decimal volume24h = 0, volume = 0;
            DateTimeOffset? ends = null;
            foreach (var m in marketsJson.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object
                    || Json.String(m, "status") is { } status && status is not ("active" or "open")
                    || KalshiChance(m) is not { } chance)
                    continue;
                var close = Json.Iso(m, "close_time");
                if (close is { } c && c - now < MinTimeLeft)
                    continue;
                var label = Json.String(m, "yes_sub_title")?.Trim() is { Length: > 0 } y ? y : Json.String(m, "subtitle")?.Trim();
                if (string.IsNullOrEmpty(label))
                    continue;
                var last = Dollars(m, "last_price");
                var previous = Dollars(m, "previous_price");
                var change = last is { } l && previous is { } p && p > 0 ? l - p : (decimal?)null;
                outcomes.Add((new OddsOutcome(label, chance, change), Json.Decimal(m, "floor_strike") ?? Json.Decimal(m, "cap_strike") ?? 0));
                // A contract pays $1, so contracts traded times the price is roughly the dollars traded.
                volume24h += (Count(m, "volume_24h") ?? 0) * chance;
                volume += (Count(m, "volume") ?? 0) * chance;
                if (close is { } closeTime && (ends is null || closeTime < ends))
                    ends = closeTime;
            }
            if (outcomes.Count == 0)
                continue;
            list.Add(new OddsMarket(
                Id: "ks:" + ticker,
                Title: title,
                Url: "https://kalshi.com/markets/" + series.ToLowerInvariant(),
                Source: "Kalshi",
                Ends: ends,
                Volume24hUsd: Math.Round(volume24h),
                VolumeUsd: Math.Round(volume),
                // Price ladders come in any order; lowest strike first reads naturally.
                Outcomes: outcomes.OrderBy(o => o.Strike).Select(o => o.Outcome).ToList(),
                Exclusive: Bool(e, "mutually_exclusive") == true,
                Coins: []));
        }
        return list;
    }

    /// <summary>The middle of the best bid and ask when both are there, otherwise the last trade.</summary>
    private static decimal? KalshiChance(JsonElement m)
    {
        var bid = Dollars(m, "yes_bid");
        var ask = Dollars(m, "yes_ask");
        if (bid is > 0 and <= 1 && ask is > 0 and <= 1 && ask >= bid)
            return (bid.Value + ask.Value) / 2;
        return Dollars(m, "last_price") is { } last and >= 0 and <= 1 ? last : null;
    }

    /// <summary>Kalshi's "_dollars" strings, or the older whole-cent numbers.</summary>
    private static decimal? Dollars(JsonElement m, string name) =>
        Json.Decimal(m, name + "_dollars") ?? (Json.Decimal(m, name) is { } cents ? cents / 100m : null);

    /// <summary>Kalshi's "_fp" strings, or the older whole numbers.</summary>
    private static decimal? Count(JsonElement m, string name) =>
        Json.Decimal(m, name + "_fp") ?? Json.Decimal(m, name);

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException or InvalidOperationException or FormatException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);
}

/// <summary>
/// Notices when a market the user pinned swings. Each outcome starts from the chance it had when first seen;
/// a move of <see cref="Threshold"/> or more from there fires once and becomes the new starting point.
/// </summary>
public sealed class OddsMoveWatcher
{
    public const decimal Threshold = 0.10m;

    private readonly Dictionary<string, decimal> _baseline = new(StringComparer.Ordinal);

    /// <summary>Outcomes of <paramref name="market"/> that moved enough since last time, with where they started.</summary>
    public List<(OddsOutcome Outcome, decimal From)> Check(OddsMarket market)
    {
        var moved = new List<(OddsOutcome, decimal)>();
        foreach (var o in market.Outcomes)
        {
            var key = market.Id + "|" + o.Label;
            if (!_baseline.TryGetValue(key, out var from))
            {
                _baseline[key] = o.Chance;
                continue;
            }
            if (Math.Abs(o.Chance - from) >= Threshold)
            {
                moved.Add((o, from));
                _baseline[key] = o.Chance;
            }
        }
        return moved;
    }

    /// <summary>Forgets a market, so pinning it again starts fresh.</summary>
    public void Forget(string marketId)
    {
        foreach (var key in _baseline.Keys.Where(k => k.StartsWith(marketId + "|", StringComparison.Ordinal)).ToList())
            _baseline.Remove(key);
    }
}
