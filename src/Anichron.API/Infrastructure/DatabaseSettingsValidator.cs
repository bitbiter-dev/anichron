using Anichron.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Anichron.API.Infrastructure;

internal sealed class DatabaseSettingsValidator : IValidateOptions<DatabaseConnectionSettings>
{
    public ValidateOptionsResult Validate(string? name, DatabaseConnectionSettings options)
    {
        var missing = options.MissingSettings();
        return missing.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(missing);
    }
}
