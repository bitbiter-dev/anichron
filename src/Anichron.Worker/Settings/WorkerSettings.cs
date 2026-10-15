using System.ComponentModel.DataAnnotations;

namespace Anichron.Worker.Settings;

public sealed record WorkerSettings
{
    // Stryker disable once String
    public string User { get; init; } = string.Empty;

    // Stryker disable once String
    public string RootPath { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int CrawlIntervalHours { get; init; } = 4;

    [Range(1, int.MaxValue)]
    public int MaxConcurrentFiles { get; init; } = 4;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double TokenCleanupIntervalHours { get; init; } = 24;

    [Required]
    public string ProxyPath { get; init; } = "/data/proxies";

    [Range(1, int.MaxValue)]
    public int ThumbnailShortEdge { get; init; } = 300;

    [Range(1, 100)]
    public int ThumbnailJpegQuality { get; init; } = 75;

    [Range(1, int.MaxValue)]
    public int PreviewLongEdge { get; init; } = 1920;

    [Range(1, 100)]
    public int PreviewJpegQuality { get; init; } = 85;

    [Range(1, int.MaxValue)]
    public int BlurhashSampleLongEdge { get; init; } = 64;

    [Required]
    public string FfmpegPath { get; init; } = "ffmpeg";

    [Range(1, int.MaxValue)]
    public int VideoMaxHeight { get; init; } = 720;

    [Range(1, int.MaxValue)]
    public int VideoBitrateKbps { get; init; } = 2000;
}
