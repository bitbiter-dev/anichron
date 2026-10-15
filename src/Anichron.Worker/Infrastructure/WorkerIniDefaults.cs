using Anichron.Infrastructure.Configuration;

namespace Anichron.Worker.Infrastructure;

internal static class WorkerIniDefaults
{
    internal static IReadOnlyList<IniEntry> Entries => [
        new IniEntry("Worker", "User", () => string.Empty),
        new IniEntry("Worker", "ExcludedDirectories", () => string.Empty),
        new IniEntry("Worker", "CrawlIntervalHours", () => "4"),
        new IniEntry("Worker", "MaxConcurrentFiles", () => "4"),
        new IniEntry("Worker", "TokenCleanupIntervalHours", () => "24"),
        new IniEntry("Worker", "ProxyPath", () => "/data/proxies"),
        new IniEntry("Worker", "ThumbnailShortEdge", () => "300"),
        new IniEntry("Worker", "ThumbnailJpegQuality", () => "75"),
        new IniEntry("Worker", "PreviewLongEdge", () => "1920"),
        new IniEntry("Worker", "PreviewJpegQuality", () => "85"),
        new IniEntry("Worker", "BlurhashSampleLongEdge", () => "64"),
        new IniEntry("Worker", "FfmpegPath", () => "ffmpeg"),
        new IniEntry("Worker", "VideoMaxHeight", () => "720"),
        new IniEntry("Worker", "VideoBitrateKbps", () => "2000"),
    ];
}
