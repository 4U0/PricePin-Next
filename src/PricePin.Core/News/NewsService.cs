using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PricePin.Core.Feeds;

namespace PricePin.Core.News;

/// <summary>One headline, with the coins it mentions.</summary>
public sealed record NewsItem(string Title, string Url, string Source, DateTimeOffset Published, IReadOnlyList<string> Coins);

/// <summary>A coin to look for in headlines: its key, symbol and name.</summary>
public sealed record NewsCoin(string Key, string Symbol, string Name);

/// <summary>
/// Crypto headlines from the free RSS feeds of well-known news sites. No key or account is
/// needed. Each headline is tagged with the coins it mentions, by name ("Solana") or by a
/// symbol written in capitals ("SOL").
/// </summary>
public sealed partial class NewsService
{
    public static IReadOnlyList<(string Name, string Url)> Feeds { get; } =
    [
        ("CoinDesk", "https://www.coindesk.com/arc/outboundfeeds/rss/"),
        ("Cointelegraph", "https://cointelegraph.com/rss"),
        ("Decrypt", "https://decrypt.co/feed"),
        ("The Block", "https://www.theblock.co/rss.xml"),
        ("Bitcoin Magazine", "https://bitcoinmagazine.com/.rss/full/"),
    ];

    /// <summary>Headlines older than this are dropped.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(2);

    /// <summary>Fetches every feed in parallel. A feed that fails is skipped; the rest still count.</summary>
    /// <returns>Headlines newest first, and how many feeds answered.</returns>
    public async Task<(IReadOnlyList<NewsItem> Items, int FeedsOk)> FetchAsync(IReadOnlyList<NewsCoin> coins, CancellationToken ct = default)
    {
        var tasks = Feeds.Select(f => FetchFeedAsync(f.Name, f.Url, ct)).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        var all = tasks.Where(t => t.Result is not null).SelectMany(t => t.Result!).ToList();
        var ok = tasks.Count(t => t.Result is not null);
        return (Merge(all, coins, DateTimeOffset.UtcNow), ok);
    }

    internal static async Task<IReadOnlyList<NewsItem>?> FetchFeedAsync(string source, string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8");
            using var response = await Http.Client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseFeed(xml, source);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Reads RSS 2.0 or Atom. Coins are tagged later, in <see cref="Merge"/>.</summary>
    internal static IReadOnlyList<NewsItem> ParseFeed(string xml, string source)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersFromEntities = 0 };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var doc = XDocument.Load(reader);
        var items = new List<NewsItem>();

        foreach (var item in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            string? Child(string name) => item.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

            var title = CleanTitle(Child("title"));
            var link = Child("link");
            if (string.IsNullOrWhiteSpace(link))
            {
                // Atom: <link href="..." rel="alternate" />
                link = item.Elements().Where(e => e.Name.LocalName == "link")
                    .OrderBy(e => (string?)e.Attribute("rel") is null or "alternate" ? 0 : 1)
                    .Select(e => (string?)e.Attribute("href"))
                    .FirstOrDefault();
            }
            link = link?.Trim();
            if (title.Length == 0 || link is null || !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;

            var published = ParseDate(Child("pubDate") ?? Child("published") ?? Child("updated") ?? Child("date"));
            items.Add(new NewsItem(title, link, source, published ?? DateTimeOffset.MinValue, []));
        }
        return items;
    }

    /// <summary>Drops old and duplicate headlines, tags coins, newest first.</summary>
    internal static IReadOnlyList<NewsItem> Merge(IEnumerable<NewsItem> items, IReadOnlyList<NewsCoin> coins, DateTimeOffset now)
    {
        var matchers = BuildMatchers(coins);
        return items
            .Where(i => i.Published > now - MaxAge && i.Published < now + TimeSpan.FromHours(1))
            .GroupBy(i => DedupeKey(i.Title))
            .Select(g => g.OrderBy(i => i.Published).First())
            .OrderByDescending(i => i.Published)
            .Take(120)
            .Select(i => i with { Coins = Tag(i.Title, matchers) })
            .ToList();
    }

    private sealed record Matcher(string Key, Regex Pattern);

    private static List<Matcher> BuildMatchers(IReadOnlyList<NewsCoin> coins)
    {
        var list = new List<Matcher>();
        foreach (var c in coins.DistinctBy(c => c.Key))
        {
            var parts = new List<string>();
            // Names are matched in any case ("bitcoin"), but only whole words and names long enough
            // not to be ordinary words.
            // Names that are also ordinary words ("stellar results") must keep their capital.
            if (c.Name.Length >= 4 && !string.Equals(c.Name, c.Symbol, StringComparison.OrdinalIgnoreCase))
                parts.Add(CommonNames.Contains(c.Name) ? Regex.Escape(c.Name) : "(?i:" + Regex.Escape(c.Name) + ")");
            // Symbols only when written in capitals and at least 3 letters: "SOL" but not "op" or "one".
            if (c.Symbol.Length >= 3 && c.Symbol.All(char.IsAsciiLetterOrDigit) && !CommonWords.Contains(c.Symbol))
                parts.Add(Regex.Escape(c.Symbol.ToUpperInvariant()));
            if (parts.Count > 0)
                list.Add(new Matcher(c.Key, new Regex(@"(?<![\p{L}\p{N}])(" + string.Join("|", parts) + @")(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)));
        }
        return list;
    }

    // Symbols that are also everyday capitalised words in headlines.
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "THE", "AND", "FOR", "NOW", "NEW", "TOP", "BIG", "ALL", "CEO", "SEC", "ETF", "USA", "WAR", "AI", "GAS", "FUN", "WIN", "ONE", "KEY", "ACT", "LAW",
    };

    private static readonly HashSet<string> CommonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stellar", "Cosmos", "Optimism", "Avalanche",
    };

    internal static IReadOnlyList<string> Tag(string title, IReadOnlyList<NewsCoin> coins) => Tag(title, BuildMatchers(coins));

    private static IReadOnlyList<string> Tag(string title, List<Matcher> matchers) =>
        matchers.Where(m => m.Pattern.IsMatch(title)).Select(m => m.Key).Take(4).ToList();

    private static string DedupeKey(string title) =>
        new string(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string CleanTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var text = WebUtility.HtmlDecode(TagRegex().Replace(raw, ""));
        return SpaceRegex().Replace(text, " ").Trim();
    }

    internal static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
            return d;
        // RFC 822 with a zone name, e.g. "Sat, 14 Nov 2026 18:05:00 GMT" or "... EST".
        var zones = new Dictionary<string, string> { ["GMT"] = "+00:00", ["UTC"] = "+00:00", ["UT"] = "+00:00", ["EST"] = "-05:00", ["EDT"] = "-04:00", ["CST"] = "-06:00", ["CDT"] = "-05:00", ["PST"] = "-08:00", ["PDT"] = "-07:00" };
        foreach (var (name, offset) in zones)
        {
            if (text.EndsWith(" " + name, StringComparison.Ordinal)
                && DateTimeOffset.TryParse(text[..^name.Length] + offset, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d;
        }
        return null;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}
