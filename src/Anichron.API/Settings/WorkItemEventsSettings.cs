using System.ComponentModel.DataAnnotations;

namespace Anichron.API.Settings;

public sealed record WorkItemEventsSettings
{
    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double IntervalSeconds { get; init; } = 2;
}
