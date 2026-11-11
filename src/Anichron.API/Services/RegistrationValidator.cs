using Anichron.API.Settings;
using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace Anichron.API.Services;

public interface IRegistrationValidator
{
    IIdentityRejection? ValidateIdentity(string username, string email);
    Task<IRegisterError?> ValidateAsync(string username, string email, string password, CancellationToken ct);
    Task<PasswordRejected?> ValidatePasswordAsync(string password, CancellationToken ct);
}

public sealed class RegistrationValidator(
    IOptions<PasswordPolicy> passwordPolicyOptions,
    IOptions<UsernamePolicy> usernamePolicyOptions,
    IPwnedPasswordClient pwnedClient) : IRegistrationValidator
{
    private readonly PasswordPolicy passwordPolicy = passwordPolicyOptions.Value;
    private readonly UsernamePolicy usernamePolicy = usernamePolicyOptions.Value;

    public IIdentityRejection? ValidateIdentity(string username, string email)
    {
        if (username.Length < usernamePolicy.MinLength
            || username.Length > usernamePolicy.MaxLength
            || !UsernamePolicy.AllowedCharacters().IsMatch(username))
        {
            return new InvalidUsername();
        }

        if (!IsValidEmail(email))
            return new InvalidEmail();

        return null;
    }

    public async Task<IRegisterError?> ValidateAsync(string username, string email, string password, CancellationToken ct)
        => ValidateIdentity(username, email) ?? (IRegisterError?)await ValidatePasswordAsync(password, ct);

    public async Task<PasswordRejected?> ValidatePasswordAsync(string password, CancellationToken ct)
    {
        if (password.Length < passwordPolicy.MinLength)
            return new PasswordTooShort();

        if (password.Length > passwordPolicy.MaxLength)
            return new PasswordTooLong();

        // IDE0046 suppressed: collapsing an async condition into a ternary reduces readability
#pragma warning disable IDE0046
        if (passwordPolicy.CheckPwnedPasswords && await pwnedClient.IsPwnedAsync(password, ct))
#pragma warning restore IDE0046
            return new PasswordPwned();

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
