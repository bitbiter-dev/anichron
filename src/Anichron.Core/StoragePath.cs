namespace Anichron.Core;

public static class StoragePath
{
    public const string OriginalsRoot = "/data/originals";

    public static string? Canonicalize(string path)
    {
        var trimmed = path.Trim();
        return Path.IsPathFullyQualified(trimmed)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed))
            : null;
    }

    public static bool IsInsideOriginals(string canonicalPath)
        => canonicalPath.StartsWith(OriginalsRoot + "/", StringComparison.Ordinal);

    public static string? FindOverlap(IEnumerable<string> canonicalRoots, string canonicalPath)
        => canonicalRoots.FirstOrDefault(root => Overlaps(root, canonicalPath));

    public static bool Overlaps(string firstCanonicalPath, string secondCanonicalPath)
        => IsSameOrDescendant(firstCanonicalPath, secondCanonicalPath)
           || IsSameOrDescendant(secondCanonicalPath, firstCanonicalPath);

    private static bool IsSameOrDescendant(string path, string ancestor)
        => path.Equals(ancestor, StringComparison.Ordinal)
           || path.StartsWith(ancestor + "/", StringComparison.Ordinal);
}
