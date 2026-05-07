namespace Anichron.API.Settings;

public sealed record PasswordPolicy
{
    public int MinLength { get; init; } = AppDefaults.Password.MinLength;
    public int MaxLength { get; init; } = AppDefaults.Password.MaxLength;
    public bool CheckPwnedPasswords { get; init; } = AppDefaults.Password.CheckPwned;

    internal string TooShortMessage => $"Password must be at least {MinLength} characters.";
    internal string TooLongMessage => $"Password must be no longer than {MaxLength} characters.";
}
