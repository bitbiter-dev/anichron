using Anichron.API.Settings;
using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace Anichron.API.Services;

public interface IRegistrationValidator
{
    AuthError? ValidateIdentity(string username, string email);
    Task<AuthError?> ValidateAsync(string username, string email, string password, CancellationToken ct);
    Task<AuthError?> ValidatePasswordAsync(string password, CancellationToken ct);
}

public sealed class RegistrationValidator(
    IOptions<PasswordPolicy> passwordPolicyOptions,
    IOptions<UsernamePolicy> usernamePolicyOptions,
    IPwnedPasswordClient pwnedClient) : IRegistrationValidator
{
    private readonly PasswordPolicy passwordPolicy = passwordPolicyOptions.Value;
    private readonly UsernamePolicy usernamePolicy = usernamePolicyOptions.Value;

    public AuthError? ValidateIdentity(string username, string email)
    {
        if (username.Length < usernamePolicy.MinLength
            || username.Length > usernamePolicy.MaxLength
            || !UsernamePolicy.AllowedCharacters().IsMatch(username))
        {
            return AuthError.InvalidUsername;
        }

        if (!IsValidEmail(email))
            return AuthError.InvalidEmail;

        return null;
    }

    public Task<AuthError?> ValidateAsync(string username, string email, string password, CancellationToken ct)
        => ValidateIdentity(username, email) is { } identityError
            ? Task.FromResult<AuthError?>(identityError)
            : ValidatePasswordRulesAsync(password, ct);

    public Task<AuthError?> ValidatePasswordAsync(string password, CancellationToken ct)
        => ValidatePasswordRulesAsync(password, ct);

    private async Task<AuthError?> ValidatePasswordRulesAsync(string password, CancellationToken ct)
    {
        if (password.Length < passwordPolicy.MinLength)
            return AuthError.PasswordTooShort;

        if (password.Length > passwordPolicy.MaxLength)
            return AuthError.PasswordTooLong;

        // IDE0046 suppressed: collapsing an async condition into a ternary reduces readability
#pragma warning disable IDE0046
        if (passwordPolicy.CheckPwnedPasswords && await pwnedClient.IsPwnedAsync(password, ct))
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
            var mailAddress = new MailAddress(email.Trim());
            return mailAddress.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
