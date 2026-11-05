using System.Diagnostics;

namespace Anichron.API.Services;

internal static class PasswordSettingCheck
{
    internal static async Task EnsureMeetsPolicyAsync(
        this IRegistrationValidator validator, string setting, string value, CancellationToken ct)
    {
        var broken = await validator.ValidatePasswordAsync(value, ct) switch
        {
            null => null,
            AuthError.PasswordTooShort => "is too short",
            AuthError.PasswordTooLong => "is too long",
            AuthError.PasswordPwned => "is known to be breached",
            var other => throw new UnreachableException($"Unexpected AuthError in a password setting: {other}"),
        };
        if (broken is not null)
            throw new InvalidOperationException($"{setting} {broken}.");
    }
}
