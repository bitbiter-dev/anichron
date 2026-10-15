using Anichron.API.Infrastructure;
using Anichron.API.Settings;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class AuthMessagesTests
{
    [Fact]
    public void AccountTemporarilyLocked_OneSecond_IsSingular()
        => AuthMessages.AccountTemporarilyLocked(1)
            .Should().Be("Too many failed login attempts. Please try again in 1 second.");

    [Fact]
    public void AccountTemporarilyLocked_SeveralSeconds_IsPlural()
        => AuthMessages.AccountTemporarilyLocked(2)
            .Should().Be("Too many failed login attempts. Please try again in 2 seconds.");

    [Fact]
    public void UsernamePolicy_InvalidFormatMessage_StatesTheLengthBoundsAndAllowedCharacters()
        => new UsernamePolicy { MinLength = 3, MaxLength = 32 }.InvalidFormatMessage
            .Should().Be("Username must be 3–32 characters and may only contain letters, digits, underscores, and hyphens.");
}
