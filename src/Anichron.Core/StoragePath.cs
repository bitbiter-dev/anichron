namespace Anichron.Core;

public static class StoragePath
{
    public static string? Canonicalize(string path)
    {
        var trimmed = path.Trim();
        return Path.IsPathFullyQualified(trimmed)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed))
            : null;
    }
}
