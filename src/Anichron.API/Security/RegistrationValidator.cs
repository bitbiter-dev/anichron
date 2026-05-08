using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace Anichron.API.Security;

public interface IRegistrationValidator
{
    Task<AuthError?> ValidateAsync(string username, string email, string password, CancellationToken ct);
    Task<AuthError?> ValidatePasswordAsync(string password, CancellationToken ct);
}

public sealed class RegistrationValidator(
    IOptions<PasswordPolicy> passwordPolicyOptions,
    IOptions<UsernamePolicy> usernamePolicyOptions,
    IPwnedPasswordClient pwnedClient) : IRegistrationValidator
{
    private readonly PasswordPolicy _passwordPolicy = passwordPolicyOptions.Value;
    private readonly UsernamePolicy _usernamePolicy = usernamePolicyOptions.Value;

    public async Task<AuthError?> ValidateAsync(string username, string email, string password, CancellationToken ct)
    {
        if (username.Length < _usernamePolicy.MinLength
            || username.Length > _usernamePolicy.MaxLength
            || !UsernamePolicy.AllowedCharacters().IsMatch(username))
        {
            return AuthError.InvalidUsername;
        }

        if (!IsValidEmail(email))
            return AuthError.InvalidEmail;

        if (password.Length < _passwordPolicy.MinLength)
            return AuthError.PasswordTooShort;

        if (password.Length > _passwordPolicy.MaxLength)
            return AuthError.PasswordTooLong;

        // IDE0046 suppressed: collapsing an async condition into a ternary reduces readability
#pragma warning disable IDE0046
        if (_passwordPolicy.CheckPwnedPasswords && await pwnedClient.IsPwnedAsync(password, ct))
#pragma warning restore IDE0046
            return AuthError.PasswordPwned;

        return null;
    }

    public async Task<AuthError?> ValidatePasswordAsync(string password, CancellationToken ct)
    {
        if (password.Length < _passwordPolicy.MinLength)
            return AuthError.PasswordTooShort;

        if (password.Length > _passwordPolicy.MaxLength)
            return AuthError.PasswordTooLong;

        // IDE0046 suppressed: collapsing an async condition into a ternary reduces readability
#pragma warning disable IDE0046
        if (_passwordPolicy.CheckPwnedPasswords && await pwnedClient.IsPwnedAsync(password, ct))
#pragma warning restore IDE0046
            return AuthError.PasswordPwned;

        return null;
    }

    private static bool IsValidEmail(string email)
    {
        if (email.Length > AppDefaults.Email.MaxLength)
            return false;
        try
        {
            var addr = new MailAddress(email.Trim());
            return addr.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
