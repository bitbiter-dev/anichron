using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Crawling;

internal sealed class CrawlExclusions(IOptions<WorkerSettings> options)
{
    private static readonly string[] builtInDirectoryNames =
    [
        "@eaDir", "#recycle", "#snapshot", "@tmp",
        "@Recycle", "@Recently-Snapshot",
        "$RECYCLE.BIN", "System Volume Information", "RECYCLER",
        "lost+found",
        "files_trashbin", "files_versions",
    ];

    private static readonly string[] appLibrarySuffixes =
    [
        ".photoslibrary", ".photolibrary", ".lrdata", ".fcpbundle", ".imovielibrary",
    ];

    private readonly HashSet<string> directoryNames = new(
        [
            .. builtInDirectoryNames,
            .. options.Value.ExcludedDirectories
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ],
        StringComparer.OrdinalIgnoreCase);

    public bool IsExcludedDirectory(string name)
        => name.StartsWith('.')
           || directoryNames.Contains(name)
           || appLibrarySuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    public static bool IsExcludedFile(string name)
        => name.StartsWith('.');
}
