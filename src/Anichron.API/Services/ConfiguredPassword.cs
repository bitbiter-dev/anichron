namespace Anichron.API.Services;

internal static class ConfiguredPassword
{
    internal static async Task EnsureMeetsPolicyAsync(
        this IRegistrationValidator validator, string setting, string password, CancellationToken ct)
    {
        if (await validator.ValidatePasswordAsync(password, ct) is { } error)
            throw new InvalidOperationException($"{setting} does not meet the password policy: {error}.");
    }
}
