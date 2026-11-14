using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PricePin.Core.Coins;
using PricePin.Core.Market;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>One coin row: live price, 24h stats, sparkline and logo.</summary>
public sealed partial class CoinRowViewModel : ObservableObject
{
    private readonly string _currencyPrefix;
    private readonly string _currencySuffix;
    private decimal? _open24h;
    private double[] _history = [];

    public CoinRowViewModel(CoinInfo info, string quoteCurrency)
    {
        Symbol = info.Symbol;
        Name = info.Name;
        BadgeBrush = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(info.BrandColor)));
        (_currencyPrefix, _currencySuffix) = CurrencyAffixes(quoteCurrency);
    }

    public string Symbol { get; }
    public string Name { get; }
    public string Initial => Symbol[..1];

    /// <summary>Badge colour shown until (or instead of) a logo.</summary>
    public Brush BadgeBrush { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    private ImageSource? _logo;

    public bool HasLogo => Logo is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriceText), nameof(HasPrice))]
    private decimal? _price;

    public bool HasPrice => Price is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeText), nameof(IsUp))]
    private decimal? _changePercent24h;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private decimal? _high24h;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private decimal? _low24h;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private decimal? _volume24h;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private string? _source;

    /// <summary>24h closes plus the live price as the last point, for the sparkline.</summary>
    [ObservableProperty]
    private IReadOnlyList<double> _sparkline = [];

    /// <summary>Changes on every price move; the view starts a flash animation when it does.</summary>
    [ObservableProperty]
    private int _flashPulse;

    /// <summary>Direction of the last price move, read by the flash animation.</summary>
    public bool LastMoveUp { get; private set; }

    public bool IsUp => (ChangePercent24h ?? 0) >= 0;

    public string PriceText => Price is { } p ? _currencyPrefix + FormatNumber(p) + _currencySuffix : "—";

    public string ChangeText => ChangePercent24h is { } c
        ? (c >= 0 ? "▲ " : "▼ ") + Math.Abs(c).ToString("0.00", CultureInfo.InvariantCulture) + "%"
        : "";

    public string DetailsText
    {
        get
        {
            string F(decimal? v) => v is { } d ? _currencyPrefix + FormatNumber(d) + _currencySuffix : "—";
            var vol = Volume24h is { } v ? FormatCompact(v) + " " + Symbol : "—";
            (string Label, string Value)[] lines =
            [
                (Loc.T("Coin.High"), F(High24h)),
                (Loc.T("Coin.Low"), F(Low24h)),
                (Loc.T("Coin.Volume"), vol),
                (Loc.T("Coin.Source"), Source ?? "—"),
            ];
            var width = lines.Max(l => l.Label.Length) + 2;
            return $"{Name} ({Symbol})\n" + string.Join("\n", lines.Select(l => l.Label.PadRight(width) + l.Value));
        }
    }

    public void RefreshText() => OnPropertyChanged(nameof(DetailsText));

    /// <summary>Applies one merged update from the feed.</summary>
    public void Apply(Ticker t)
    {
        if (Price is { } previous && t.Last != previous)
        {
            LastMoveUp = t.Last > previous;
            FlashPulse++;
        }
        Price = t.Last;

        if (t.Open24h is > 0)
            _open24h = t.Open24h;
        if (t.High24h is { } h) High24h = Math.Max(h, t.Last);
        if (t.Low24h is { } l) Low24h = Math.Min(l, t.Last);
        if (t.Volume24h is { } v) Volume24h = v;
        Source = t.Source;

        // Recompute the 24h change from the live price so it moves with every trade.
        ChangePercent24h = _open24h is { } open
            ? Math.Round((t.Last - open) / open * 100m, 2)
            : t.ChangePercent24h ?? ChangePercent24h;

        UpdateSparklineTail();
    }

    public void SetHistory(IReadOnlyList<decimal> closes)
    {
        _history = closes.Select(c => (double)c).ToArray();
        UpdateSparklineTail();
    }

    private void UpdateSparklineTail()
    {
        if (_history.Length == 0)
            return;
        var points = (double[])_history.Clone();
        if (Price is { } p)
            points[^1] = (double)p;
        Sparkline = points;
    }

    /// <summary>Enough decimals for $90,000 coins and $0.0000123 coins alike.</summary>
    public static string FormatNumber(decimal price)
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
        return price.ToString(format, CultureInfo.InvariantCulture);
    }

    public static string FormatCompact(decimal value) => Math.Abs(value) switch
    {
        >= 1_000_000_000m => (value / 1_000_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + "B",
        >= 1_000_000m => (value / 1_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + "M",
        >= 1_000m => (value / 1_000m).ToString("0.##", CultureInfo.InvariantCulture) + "K",
        _ => value.ToString("0.##", CultureInfo.InvariantCulture),
    };

    private static (string Prefix, string Suffix) CurrencyAffixes(string quote) => quote switch
    {
        "USD" or "USDT" or "USDC" => ("$", ""),
        "EUR" => ("€", ""),
        "GBP" => ("£", ""),
        "TRY" => ("₺", ""),
        "JPY" => ("¥", ""),
        _ => ("", " " + quote),
    };

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}
