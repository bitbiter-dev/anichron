using System.Text.RegularExpressions;

namespace Anichron.API.Settings;

public sealed partial record UsernamePolicy
{
    public int MinLength { get; init; } = AppDefaults.Username.MinLength;
    public int MaxLength { get; init; } = AppDefaults.Username.MaxLength;

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    internal static partial Regex AllowedCharacters();

    internal string InvalidFormatMessage => $"Username must be {MinLength}–{MaxLength} characters and may only contain letters, digits, underscores, and hyphens.";
}
