using Anichron.Worker.Infrastructure;

namespace Anichron.Worker.Tests.Unit.Infrastructure;

public sealed class WorkerIniDefaultsTests
{
    [Fact]
    public void Entries_HaveTheDocumentedDefaults()
    {
        var entries = WorkerIniDefaults.Entries.Select(e => (e.Section, e.Key, Value: e.DefaultValue()));

        entries.Should().Equal(
            ("Worker", "User", ""),
            ("Worker", "RootPath", "/data/originals"),
            ("Worker", "CrawlIntervalHours", "4"),
            ("Worker", "MaxConcurrentFiles", "4"),
            ("Worker", "TokenCleanupIntervalHours", "24"),
            ("Worker", "ProxyPath", "/data/proxies"),
            ("Worker", "ThumbnailShortEdge", "300"),
            ("Worker", "ThumbnailJpegQuality", "75"),
            ("Worker", "PreviewLongEdge", "1920"),
            ("Worker", "PreviewJpegQuality", "85"),
            ("Worker", "BlurhashSampleLongEdge", "64"),
            ("Worker", "FfmpegPath", "ffmpeg"),
            ("Worker", "VideoMaxHeight", "720"),
            ("Worker", "VideoBitrateKbps", "2000"));
    }
}
