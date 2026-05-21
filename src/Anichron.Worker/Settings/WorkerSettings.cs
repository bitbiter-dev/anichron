using Microsoft.Extensions.Options;

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

internal sealed class WorkerSettingsValidator : IValidateOptions<WorkerSettings>
{
    public ValidateOptionsResult Validate(string? name, WorkerSettings options)
    {
        var failures = new List<string>();

        if (options.MaxConcurrentFiles <= 0)
            failures.Add($"{nameof(WorkerSettings.MaxConcurrentFiles)} must be > 0");
        if (options.CrawlIntervalHours <= 0)
            failures.Add($"{nameof(WorkerSettings.CrawlIntervalHours)} must be > 0");
        if (options.ThumbnailMaxWidth <= 0)
            failures.Add($"{nameof(WorkerSettings.ThumbnailMaxWidth)} must be > 0");
        if (options.ThumbnailJpegQuality is < 1 or > 100)
            failures.Add($"{nameof(WorkerSettings.ThumbnailJpegQuality)} must be between 1 and 100");
        if (options.PreviewMaxWidth <= 0)
            failures.Add($"{nameof(WorkerSettings.PreviewMaxWidth)} must be > 0");
        if (options.PreviewJpegQuality is < 1 or > 100)
            failures.Add($"{nameof(WorkerSettings.PreviewJpegQuality)} must be between 1 and 100");
        if (options.BlurhashSampleWidth <= 0)
            failures.Add($"{nameof(WorkerSettings.BlurhashSampleWidth)} must be > 0");
        if (options.VideoMaxHeight <= 0)
            failures.Add($"{nameof(WorkerSettings.VideoMaxHeight)} must be > 0");
        if (options.VideoBitrateKbps <= 0)
            failures.Add($"{nameof(WorkerSettings.VideoBitrateKbps)} must be > 0");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
