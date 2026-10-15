using Anichron.Worker.Settings;
using System.ComponentModel.DataAnnotations;

namespace Anichron.Worker.Tests.Unit.Settings;

public sealed class WorkerSettingsValidatorTests
{
    private static readonly WorkerSettings ValidSettings = new()
    {
        MaxConcurrentFiles = 4,
        CrawlIntervalHours = 4,
        ThumbnailShortEdge = 300,
        ThumbnailJpegQuality = 75,
        PreviewLongEdge = 1920,
        PreviewJpegQuality = 85,
        BlurhashSampleLongEdge = 64,
        VideoMaxHeight = 720,
        VideoBitrateKbps = 2000,
        TokenCleanupIntervalHours = 24,
        FfmpegPath = "ffmpeg",
        ProxyPath = "/data/proxies",
    };

    private static bool IsValid(WorkerSettings settings)
        => Validator.TryValidateObject(settings, new ValidationContext(settings), null, validateAllProperties: true);

    private static IReadOnlyList<ValidationResult> GetFailures(WorkerSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }

    // ==========================================================================
    // Valid settings
    // ==========================================================================

    [Fact]
    public void Validate_AllValid_ReturnsSuccess()
    {
        IsValid(ValidSettings).Should().BeTrue();
    }

    // ==========================================================================
    // CrawlIntervalHours
    // ==========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("Lightroom Previews, tmp")]
    public void Validate_ExcludedDirectoriesAsFolderNames_ReturnsSuccess(string excludedDirectories)
        => IsValid(ValidSettings with { ExcludedDirectories = excludedDirectories }).Should().BeTrue();

    [Theory]
    [InlineData("2024/Private")]
    [InlineData("tmp, Lightroom Previews/")]
    [InlineData(@"Exports\\Old")]
    public void Validate_ExcludedDirectoriesContainingAPath_ReturnsFailedNamingTheSetting(string excludedDirectories)
        => GetFailures(ValidSettings with { ExcludedDirectories = excludedDirectories })
            .Should().ContainSingle()
            .Which.MemberNames.Should().Equal(nameof(WorkerSettings.ExcludedDirectories));

    [Fact]
    public void Validate_CrawlIntervalHoursZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { CrawlIntervalHours = 0 }).Should().BeFalse();
    }

    // ==========================================================================
    // MaxConcurrentFiles
    // ==========================================================================

    [Fact]
    public void Validate_MaxConcurrentFilesZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { MaxConcurrentFiles = 0 }).Should().BeFalse();
    }

    // ==========================================================================
    // ThumbnailShortEdge / PreviewLongEdge / BlurhashSampleLongEdge
    // ==========================================================================

    [Fact]
    public void Validate_ThumbnailShortEdgeZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { ThumbnailShortEdge = 0 }).Should().BeFalse();
    }

    [Fact]
    public void Validate_PreviewLongEdgeZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { PreviewLongEdge = 0 }).Should().BeFalse();
    }

    [Fact]
    public void Validate_BlurhashSampleLongEdgeZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { BlurhashSampleLongEdge = 0 }).Should().BeFalse();
    }

    // ==========================================================================
    // JPEG quality bounds
    // ==========================================================================

    [Fact]
    public void Validate_ThumbnailJpegQualityZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { ThumbnailJpegQuality = 0 }).Should().BeFalse();
    }

    [Fact]
    public void Validate_ThumbnailJpegQuality101_ReturnsFailed()
    {
        IsValid(ValidSettings with { ThumbnailJpegQuality = 101 }).Should().BeFalse();
    }

    [Fact]
    public void Validate_PreviewJpegQualityZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { PreviewJpegQuality = 0 }).Should().BeFalse();
    }

    [Fact]
    public void Validate_PreviewJpegQuality101_ReturnsFailed()
    {
        IsValid(ValidSettings with { PreviewJpegQuality = 101 }).Should().BeFalse();
    }

    // ==========================================================================
    // Video settings
    // ==========================================================================

    [Fact]
    public void Validate_VideoMaxHeightZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { VideoMaxHeight = 0 }).Should().BeFalse();
    }

    [Fact]
    public void Validate_VideoBitrateKbpsZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { VideoBitrateKbps = 0 }).Should().BeFalse();
    }

    // ==========================================================================
    // TokenCleanupIntervalHours
    // ==========================================================================

    [Fact]
    public void Validate_TokenCleanupIntervalHoursZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { TokenCleanupIntervalHours = 0 }).Should().BeFalse();
    }

    // ==========================================================================
    // FfmpegPath
    // ==========================================================================

    [Fact]
    public void Validate_FfmpegPathEmpty_ReturnsFailed()
    {
        IsValid(ValidSettings with { FfmpegPath = string.Empty }).Should().BeFalse();
    }

    // ==========================================================================
    // ProxyPath
    // ==========================================================================

    [Fact]
    public void Validate_ProxyPathEmpty_ReturnsFailed()
    {
        IsValid(ValidSettings with { ProxyPath = string.Empty }).Should().BeFalse();
    }

    // ==========================================================================
    // Multiple failures
    // ==========================================================================

    [Fact]
    public void Validate_MultipleInvalidSettings_ReportsAllFailures()
    {
        var failures = GetFailures(ValidSettings with { ThumbnailJpegQuality = 0, VideoBitrateKbps = -1 });

        failures.Should().HaveCount(2);
    }

    [Fact]
    public void Defaults_PointAtTheDocumentedLocationsAndTools()
    {
        var settings = new WorkerSettings();

        settings.RootPath.Should().BeEmpty();
        settings.ProxyPath.Should().Be("/data/proxies");
        settings.FfmpegPath.Should().Be("ffmpeg");
    }
}
