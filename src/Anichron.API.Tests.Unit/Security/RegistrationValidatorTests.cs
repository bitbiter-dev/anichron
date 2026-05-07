using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.API.Tests.Unit.Security;

public sealed class RegistrationValidatorTests
{
    private const string ValidUsername = "validuser";
    private const string ValidEmail = "user@example.com";
    private const string ValidPassword = "CorrectPassword1"; // 16 chars — meets default MinLength (12)

    // 245 'a' + "@example.com" = 257 chars > AppDefaults.Email.MaxLength (256)
    private const string TooLongEmail =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" +
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" +
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" +
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" +
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" +
        "@example.com";

    [Theory]
    [InlineData("")]                                   // 0 chars — below MinLength (3)
    [InlineData("ab")]                                 // 2 chars — below MinLength (3)
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]  // 33 chars — above MaxLength (32)
    [InlineData("user name")]                          // space not in [a-zA-Z0-9_-]
    [InlineData("user.name")]                          // period not allowed
    [InlineData("üser")]                               // non-ASCII
    public async Task ValidateAsync_InvalidUsername_ReturnsInvalidUsername(string username)
    {
        var testee = new TestFixture().CreateTestee();

        var result = await testee.ValidateAsync(username, ValidEmail, ValidPassword, CancellationToken.None);

        result.Should().Be(AuthError.InvalidUsername);
    }

    [Theory]
    [InlineData("")]
    [InlineData("notanemail")]
    [InlineData("@domain.com")]
    [InlineData("user@")]
    [InlineData("John Doe <user@example.com>")] // display-name form: addr.Address ≠ raw input
    public async Task ValidateAsync_InvalidEmail_ReturnsInvalidEmail(string email)
    {
        var testee = new TestFixture().CreateTestee();

        var result = await testee.ValidateAsync(ValidUsername, email, ValidPassword, CancellationToken.None);

        result.Should().Be(AuthError.InvalidEmail);
    }

    [Fact]
    public async Task ValidateAsync_EmailTooLong_ReturnsInvalidEmail()
    {
        var testee = new TestFixture().CreateTestee();

        var result = await testee.ValidateAsync(ValidUsername, TooLongEmail, ValidPassword, CancellationToken.None);

        result.Should().Be(AuthError.InvalidEmail);
    }

    [Theory]
    [InlineData("short12345")] // 10 chars — below MinLength (12)
    [InlineData("")]           // 0 chars — rejected before reaching Argon2
    public async Task ValidateAsync_PasswordTooShort_ReturnsPasswordTooShort(string password)
    {
        var testee = new TestFixture().CreateTestee();

        var result = await testee.ValidateAsync(ValidUsername, ValidEmail, password, CancellationToken.None);

        result.Should().Be(AuthError.PasswordTooShort);
    }

    [Fact]
    public async Task ValidateAsync_PasswordTooLong_ReturnsPasswordTooLong()
    {
        var testee = new TestFixture()
            .WithPasswordPolicy(new PasswordPolicy { MaxLength = 10 })
            .CreateTestee();

        var result = await testee.ValidateAsync(ValidUsername, ValidEmail, "12characters", CancellationToken.None);

        result.Should().Be(AuthError.PasswordTooLong);
    }

    [Fact]
    public async Task ValidateAsync_PasswordIsPwned_ReturnsPasswordPwned()
    {
        var testee = new TestFixture()
            .WithPwnedPassword(isPwned: true)
            .CreateTestee();

        var result = await testee.ValidateAsync(ValidUsername, ValidEmail, ValidPassword, CancellationToken.None);

        result.Should().Be(AuthError.PasswordPwned);
    }

    [Fact]
    public async Task ValidateAsync_PwnedCheckDisabledAndPasswordPwned_ReturnsNull()
    {
        var testee = new TestFixture()
            .WithPwnedPassword(isPwned: true)
            .WithPasswordPolicy(new PasswordPolicy { CheckPwnedPasswords = false })
            .CreateTestee();

        var result = await testee.ValidateAsync(ValidUsername, ValidEmail, ValidPassword, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_AllInputsValid_ReturnsNull()
    {
        var testee = new TestFixture()
            .WithPwnedPassword(isPwned: false)
            .CreateTestee();

        var result = await testee.ValidateAsync(ValidUsername, ValidEmail, ValidPassword, CancellationToken.None);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("abc", "CorrectPassword1")] // username at MinLength (3)
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdef", "CorrectPassword1")] // username at MaxLength (32)
    [InlineData("validuser", "123456789012")]    // password at MinLength (12)
    public async Task ValidateAsync_UsernameAndPasswordAtBoundary_ReturnsNull(string username, string password)
    {
        var testee = new TestFixture()
            .WithPwnedPassword(isPwned: false)
            .CreateTestee();

        var result = await testee.ValidateAsync(username, ValidEmail, password, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_PwnedClientThrows_PropagatesException()
    {
        var fixture = new TestFixture();
        fixture.PwnedClient
            .IsPwnedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new HttpRequestException("Network error")));
        var testee = fixture.CreateTestee();

        var act = async () => await testee.ValidateAsync(ValidUsername, ValidEmail, ValidPassword, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class TestFixture
    {
        private PasswordPolicy _passwordPolicy = new();
        public IPwnedPasswordClient PwnedClient { get; } = Substitute.For<IPwnedPasswordClient>();

        public TestFixture WithPasswordPolicy(PasswordPolicy policy)
        {
            _passwordPolicy = policy;
            return this;
        }

        public TestFixture WithPwnedPassword(bool isPwned)
        {
            PwnedClient.IsPwnedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(isPwned);
            return this;
        }

        public RegistrationValidator CreateTestee() => new(
            Options.Create(_passwordPolicy),
            Options.Create(new UsernamePolicy()),
            PwnedClient);
    }
}
