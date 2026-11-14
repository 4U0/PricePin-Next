namespace PricePin.Core.Portfolio;

/// <summary>Coins the user owns, saved in the settings file.</summary>
public sealed class Holding
{
    public string Symbol { get; set; } = "";

    public decimal Amount { get; set; }

    /// <summary>Average price paid per coin, in <see cref="CostQuote"/>. Optional.</summary>
    public decimal? AverageCost { get; set; }

    /// <summary>Currency <see cref="AverageCost"/> is in; profit/loss shows only while prices are in it.</summary>
    public string CostQuote { get; set; } = "USD";

    public bool IsValid => !string.IsNullOrWhiteSpace(Symbol) && Amount > 0 && AverageCost is null or >= 0;
}

/// <summary>A holding priced at the latest market price.</summary>
/// <param name="Price">Latest price, or null if none has arrived yet.</param>
/// <param name="ChangePercent24h">24h change of the price, in percent.</param>
/// <param name="AverageCost">Average cost in the current currency, or null if unknown or in another currency.</param>
public sealed record PortfolioLine(string Symbol, decimal Amount, decimal? Price, decimal? ChangePercent24h, decimal? AverageCost)
{
    public decimal? Value => Price * Amount;

    /// <summary>How much the holding's value moved over 24h.</summary>
    public decimal? Change24h =>
        Price is { } p && ChangePercent24h is { } c && c > -100m ? (p - p / (1 + c / 100m)) * Amount : null;

    public decimal? Cost => AverageCost * Amount;
    public decimal? ProfitLoss => Value - Cost;
    public decimal? ProfitLossPercent => Cost is { } cost && cost > 0 && ProfitLoss is { } pl ? pl / cost * 100m : null;
}

public sealed record PortfolioTotals(
    decimal Value, decimal? Change24h, decimal? ChangePercent24h,
    decimal? Cost, decimal? ProfitLoss, decimal? ProfitLossPercent, int Unpriced);

public static class PortfolioMath
{
    /// <summary>
    /// Adds up holdings that have a price. The 24h change and profit/loss cover only the
    /// holdings they can be worked out for; <see cref="PortfolioTotals.Unpriced"/> counts the rest.
    /// </summary>
    public static PortfolioTotals Totals(IEnumerable<PortfolioLine> lines)
    {
        decimal value = 0, change = 0, changeBase = 0, cost = 0, costedValue = 0;
        bool anyChange = false, anyCost = false;
        var unpriced = 0;

        foreach (var l in lines)
        {
            if (l.Value is not { } v)
            {
                unpriced++;
                continue;
            }
            value += v;
            if (l.Change24h is { } c)
            {
                change += c;
                changeBase += v - c;
                anyChange = true;
            }
            if (l.Cost is { } k)
            {
                cost += k;
                costedValue += v;
                anyCost = true;
            }
        }

        decimal? changePct = anyChange && changeBase > 0 ? change / changeBase * 100m : null;
        decimal? pl = anyCost ? costedValue - cost : null;
        decimal? plPct = anyCost && cost > 0 ? pl / cost * 100m : null;
        return new PortfolioTotals(value, anyChange ? change : null, changePct, anyCost ? cost : null, pl, plPct, unpriced);
    }
}
