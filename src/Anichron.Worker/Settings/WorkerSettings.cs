using Anichron.Infrastructure.Configuration;
using System.ComponentModel.DataAnnotations;

namespace Anichron.Worker.Settings;

public sealed record WorkerSettings : IValidatableObject
{
    // Stryker disable once String
    public string User { get; init; } = string.Empty;

    // Stryker disable once String
    public string RootPath { get; init; } = string.Empty;

    public string ExcludedDirectories { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int CrawlIntervalHours { get; init; } = 4;

    [Range(1, int.MaxValue)]
    public int MaxConcurrentFiles { get; init; } = 4;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double TokenCleanupIntervalHours { get; init; } = 24;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double WorkPollIntervalSeconds { get; init; } = 5;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double WorkHeartbeatSeconds { get; init; } = 2;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double WorkLeaseSeconds { get; init; } = 60;

    [Range(1, int.MaxValue)]
    public int WorkMaxStalls { get; init; } = 3;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double WorkItemRetentionDays { get; init; } = 30;

    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)]
    public double WorkItemRetentionIntervalHours { get; init; } = 24;

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

    public IReadOnlyList<string> ExcludedDirectoryNames()
        => ConfigurationList.Parse(ExcludedDirectories, static name => name);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var paths = ExcludedDirectoryNames()
            .Where(name => name.IndexOfAny(['/', '\\']) >= 0)
            .Select(name => new ValidationResult(
                $"Worker:ExcludedDirectories takes folder names, not paths: '{name}'.",
                [nameof(ExcludedDirectories)]));
        return WorkLeaseSeconds > WorkHeartbeatSeconds
            ? paths
            : paths.Append(new ValidationResult(
                "Worker:WorkLeaseSeconds must be longer than Worker:WorkHeartbeatSeconds, or every lease expires between heartbeats.",
                [nameof(WorkLeaseSeconds)]));
    }
}
