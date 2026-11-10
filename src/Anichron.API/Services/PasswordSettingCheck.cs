namespace Anichron.API.Services;

internal static class PasswordSettingCheck
{
    internal static async Task EnsureMeetsPolicyAsync(
        this IRegistrationValidator validator, string setting, string value, CancellationToken ct)
    {
        var rejected = await validator.ValidatePasswordAsync(value, ct);
        if (rejected is not null)
        {
            var broken = rejected.Match(() => "is too short", () => "is too long", () => "is known to be breached");
            throw new InvalidOperationException($"{setting} {broken}.");
        }
    }
}
