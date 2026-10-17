using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Crawling;

internal sealed class CrawlExclusions(IOptions<WorkerSettings> options)
{
    private readonly HashSet<string> directoryNames = new(
        [
            .. BuiltInDirectoryNames(),
            .. options.Value.ExcludedDirectoryNames(),
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly string[] appLibrarySuffixes = AppLibrarySuffixes();

    public bool IsExcludedDirectory(string name)
        => name.StartsWith('.')
           || directoryNames.Contains(name)
           || appLibrarySuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    public static bool IsExcludedFile(string name)
        => name.StartsWith('.');

    private static string[] BuiltInDirectoryNames()
    {
        return
        [
            "@eaDir", "#recycle", "#snapshot", "@tmp",
            "@Recycle", "@Recently-Snapshot",
            "$RECYCLE.BIN", "System Volume Information", "RECYCLER",
            "lost+found",
            "files_trashbin", "files_versions",
        ];
    }

    private static string[] AppLibrarySuffixes()
    {
        return [".photoslibrary", ".photolibrary", ".lrdata", ".fcpbundle", ".imovielibrary"];
    }
}
