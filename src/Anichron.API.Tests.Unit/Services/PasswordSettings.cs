using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.API.Tests.Unit.Services;

internal static class PasswordSettings
{
    private const string BreachedPassword = "correct horse battery";

    public static TheoryData<string, string> PolicyBreaches => new()
    {
        { "elevenchars", "is too short." },
        { new string('a', 129), "is too long." },
        { BreachedPassword, "is known to be breached." },
    };

    public static RegistrationValidator Validator(IPwnedPasswordClient pwned)
    {
        pwned.IsPwnedAsync(BreachedPassword, Arg.Any<CancellationToken>()).Returns(true);
        return new RegistrationValidator(Options.Create(new PasswordPolicy()), Options.Create(new UsernamePolicy()), pwned);
    }
}
