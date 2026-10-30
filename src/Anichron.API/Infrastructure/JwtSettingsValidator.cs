using Anichron.API.Settings;
using Microsoft.Extensions.Options;
using System.Text;

namespace Anichron.API.Infrastructure;

internal sealed class JwtSettingsValidator : IValidateOptions<JwtSettings>
{
    private const int minSecretBytes = 32;

    public ValidateOptionsResult Validate(string? name, JwtSettings options)
    {
        List<string> failures = [];

        if (string.IsNullOrEmpty(options.Secret))
            failures.Add("Jwt:Secret configuration is missing.");
        else if (Encoding.UTF8.GetByteCount(options.Secret) < minSecretBytes)
            failures.Add("Jwt:Secret must be at least 32 bytes.");
        if (string.IsNullOrEmpty(options.Issuer))
            failures.Add("Jwt:Issuer is missing.");
        if (string.IsNullOrEmpty(options.Audience))
            failures.Add("Jwt:Audience is missing.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
