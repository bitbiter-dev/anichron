namespace Anichron.Core;

public static class IdentityNormalizer
{
    public static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
