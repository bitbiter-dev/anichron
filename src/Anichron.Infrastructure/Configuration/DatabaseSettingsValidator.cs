using Microsoft.Extensions.Options;

namespace Anichron.Infrastructure.Configuration;

internal sealed class DatabaseSettingsValidator : IValidateOptions<DatabaseConnectionSettings>
{
    public ValidateOptionsResult Validate(string? name, DatabaseConnectionSettings options)
    {
        var missing = options.MissingSettings();
        return missing.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(missing);
    }
}
