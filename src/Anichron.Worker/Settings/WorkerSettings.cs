namespace Anichron.Worker.Settings;

public sealed record WorkerSettings
{
    public string User { get; init; } = string.Empty;

    public string RootPath { get; init; } = "/data/originals";

    public int CrawlIntervalHours { get; init; } = 4;

    public int MaxConcurrentFiles { get; init; } = 4;

    public double TokenCleanupIntervalHours { get; init; } = 24;

    public string ProxyPath { get; init; } = "/data/proxies";

    public int ThumbnailMaxWidth { get; init; } = 300;

    public int ThumbnailJpegQuality { get; init; } = 75;

    public int PreviewMaxWidth { get; init; } = 1920;

    public int PreviewJpegQuality { get; init; } = 85;

    public int BlurhashSampleWidth { get; init; } = 64;

    public string FfmpegPath { get; init; } = "ffmpeg";

    public int VideoMaxHeight { get; init; } = 720;

    public int VideoBitrateKbps { get; init; } = 2000;
}
