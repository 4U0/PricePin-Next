using System.Text.Json.Serialization;

namespace PricePin.Core.Market;

/// <summary>The time span a widget's change and sparkline cover.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChangePeriod>))]
public enum ChangePeriod
{
    Hour,
    Day,
    Week,
}
