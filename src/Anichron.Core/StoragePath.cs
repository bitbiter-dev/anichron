namespace Anichron.Core;

public static class StoragePath
{
    public const string OriginalsRoot = "/data/originals";
    public const string ProxiesRoot = "/data/proxies";

    public static string? Canonicalize(string path)
    {
        var trimmed = path.Trim();
        return Path.IsPathFullyQualified(trimmed)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed))
            : null;
    }

    /// <summary>
    /// Joins a stored proxy path, relative to <see cref="ProxiesRoot"/>, with the root. Returns
    /// <see langword="null"/> when the path resolves outside the root.
    /// </summary>
    /// <param name="proxyPath">The path relative to the proxy root.</param>
    public static string? ResolveProxy(string proxyPath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(ProxiesRoot, proxyPath));
        return IsInside(ProxiesRoot, fullPath) ? fullPath : null;
    }

    public static bool IsInsideOriginals(string canonicalPath)
        => IsInside(OriginalsRoot, canonicalPath);

    public static bool IsInside(string root, string canonicalPath)
        => canonicalPath.StartsWith(root + "/", StringComparison.Ordinal);

    public static string? FindOverlap(IEnumerable<string> canonicalRoots, string canonicalPath)
        => canonicalRoots.FirstOrDefault(root => Overlaps(root, canonicalPath));

    public static bool Overlaps(string firstCanonicalPath, string secondCanonicalPath)
        => IsSameOrDescendant(firstCanonicalPath, secondCanonicalPath)
           || IsSameOrDescendant(secondCanonicalPath, firstCanonicalPath);

    private static bool IsSameOrDescendant(string path, string ancestor)
        => path.Equals(ancestor, StringComparison.Ordinal) || IsInside(ancestor, path);
}
