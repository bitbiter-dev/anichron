using System.ComponentModel.DataAnnotations;

namespace Anichron.API.Settings;

public sealed record RateLimitSettings
{
    [Range(1, int.MaxValue)]
    public int SensitivePermitLimit { get; init; } = AppDefaults.RateLimit.Sensitive.PermitLimit;
}
