using Anichron.Worker.Settings;
using System.ComponentModel.DataAnnotations;

namespace Anichron.Worker.Tests.Unit.Settings;

public sealed class WorkerSettingsTests
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
    [InlineData("2024/Private", "2024/Private")]
    [InlineData("/Private", "/Private")]
    [InlineData("tmp, Lightroom Previews/", "Lightroom Previews/")]
    [InlineData(@"Exports\\Old", @"Exports\\Old")]
    public void Validate_ExcludedDirectoriesContainingAPath_ReportsThatEntryByName(string excludedDirectories, string entry)
        => GetFailures(ValidSettings with { ExcludedDirectories = excludedDirectories })
            .Should().ContainSingle()
            .Which.Should().Match<ValidationResult>(r =>
                r.MemberNames.SequenceEqual(new[] { nameof(WorkerSettings.ExcludedDirectories) })
                && r.ErrorMessage == $"Worker:ExcludedDirectories takes folder names, not paths: '{entry}'.");

    [Fact]
    public void Validate_SeveralPathEntries_ReportsEachOne()
        => GetFailures(ValidSettings with { ExcludedDirectories = "a/b, ok, c\\d" })
            .Select(r => r.ErrorMessage)
            .Should().Equal(
                "Worker:ExcludedDirectories takes folder names, not paths: 'a/b'.",
                "Worker:ExcludedDirectories takes folder names, not paths: 'c\\d'.");

    [Fact]
    public void ExcludedDirectoryNames_ParsesTheCommaSeparatedSetting()
        => (ValidSettings with { ExcludedDirectories = " Lightroom Previews , ,tmp" }).ExcludedDirectoryNames()
            .Should().Equal("Lightroom Previews", "tmp");

    [Fact]
    public void Validate_CrawlIntervalHoursZero_ReturnsFailed()
    {
        IsValid(ValidSettings with { CrawlIntervalHours = 0 }).Should().BeFalse();
    }

    // ==========================================================================
    // Burst detection
    // ==========================================================================

    [Fact]
    public void Defaults_BurstGapSpanAndThreshold_AreTwoMinutesFiveMinutesAndAQuarter()
        => new WorkerSettings().Should().BeEquivalentTo(
            new { BurstGapSeconds = 120, BurstSpanSeconds = 300, BurstSimilarityThreshold = 0.25 });

    [Fact]
    public void Validate_BurstGapSecondsZero_ReturnsFailed()
        => IsValid(ValidSettings with { BurstGapSeconds = 0 }).Should().BeFalse();

    [Fact]
    public void Validate_BurstSpanSecondsZero_ReturnsFailed()
        => IsValid(ValidSettings with { BurstSpanSeconds = 0 }).Should().BeFalse();

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.01, true)]
    [InlineData(1.0, true)]
    [InlineData(1.01, false)]
    public void Validate_BurstSimilarityThreshold_MustBeAboveZeroAndAtMostOne(double threshold, bool valid)
        => IsValid(ValidSettings with { BurstSimilarityThreshold = threshold }).Should().Be(valid);

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
    // Work queue
    // ==========================================================================

    [Fact]
    public void Defaults_WorkQueueTimingsAreTheDocumentedOnes()
        => new WorkerSettings().Should().BeEquivalentTo(new
        {
            WorkPollIntervalSeconds = 5.0,
            WorkHeartbeatSeconds = 2.0,
            WorkLeaseSeconds = 60.0,
            WorkMaxStalls = 3,
            WorkItemRetentionDays = 30.0,
            WorkItemRetentionIntervalHours = 24.0,
        });

    private static WorkerSettings WithZero(string setting) => setting switch
    {
        nameof(WorkerSettings.WorkPollIntervalSeconds) => ValidSettings with { WorkPollIntervalSeconds = 0 },
        nameof(WorkerSettings.WorkHeartbeatSeconds) => ValidSettings with { WorkHeartbeatSeconds = 0 },
        nameof(WorkerSettings.WorkLeaseSeconds) => ValidSettings with { WorkLeaseSeconds = 0 },
        nameof(WorkerSettings.WorkMaxStalls) => ValidSettings with { WorkMaxStalls = 0 },
        nameof(WorkerSettings.WorkItemRetentionDays) => ValidSettings with { WorkItemRetentionDays = 0 },
        _ => ValidSettings with { WorkItemRetentionIntervalHours = 0 },
    };

    [Theory]
    [InlineData(nameof(WorkerSettings.WorkPollIntervalSeconds))]
    [InlineData(nameof(WorkerSettings.WorkHeartbeatSeconds))]
    [InlineData(nameof(WorkerSettings.WorkLeaseSeconds))]
    [InlineData(nameof(WorkerSettings.WorkMaxStalls))]
    [InlineData(nameof(WorkerSettings.WorkItemRetentionDays))]
    [InlineData(nameof(WorkerSettings.WorkItemRetentionIntervalHours))]
    public void Validate_NonPositiveWorkQueueSetting_ReportsIt(string setting)
        => GetFailures(WithZero(setting)).Should().Contain(failure => failure.MemberNames.Contains(setting));

    [Fact]
    public void Validate_SpanEqualToTheGap_ReturnsSuccess()
        => IsValid(ValidSettings with { BurstGapSeconds = 120, BurstSpanSeconds = 120 }).Should().BeTrue();

    [Fact]
    public void Validate_SpanShorterThanTheGap_ReportsTheSpan()
        => GetFailures(ValidSettings with { BurstGapSeconds = 120, BurstSpanSeconds = 119 })
            .Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                MemberNames = new[] { nameof(WorkerSettings.BurstSpanSeconds) },
                ErrorMessage = "Worker:BurstSpanSeconds must be at least Worker:BurstGapSeconds, or no neighbour further away than the span can ever join.",
            });

    [Fact]
    public void Validate_LeaseAndSpanBothInvalid_ReportsBoth()
        => GetFailures(ValidSettings with { WorkHeartbeatSeconds = 2, WorkLeaseSeconds = 2, BurstSpanSeconds = 1 })
            .Select(failure => failure.MemberNames.Single())
            .Should().Equal(nameof(WorkerSettings.WorkLeaseSeconds), nameof(WorkerSettings.BurstSpanSeconds));

    [Theory]
    [InlineData(2, 8)]
    [InlineData(10, 2)]
    public void Validate_LeaseNotLongerThanFourHeartbeats_ReportsTheLease(double heartbeat, double lease)
        => GetFailures(ValidSettings with { WorkHeartbeatSeconds = heartbeat, WorkLeaseSeconds = lease })
            .Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                MemberNames = new[] { nameof(WorkerSettings.WorkLeaseSeconds) },
                ErrorMessage = "Worker:WorkLeaseSeconds must be longer than four times Worker:WorkHeartbeatSeconds: a running item stops itself half a lease after its last heartbeat that reached the database, and a slow heartbeat may answer up to two intervals after the one before.",
            });

    [Fact]
    public void Validate_LeaseJustLongerThanFourHeartbeats_ReturnsSuccess()
        => IsValid(ValidSettings with { WorkHeartbeatSeconds = 2, WorkLeaseSeconds = 8.5 }).Should().BeTrue();

    [Fact]
    public void Validate_LeaseAndAPathEntryBothWrong_ReportsThePathFirst()
        => GetFailures(ValidSettings with { WorkLeaseSeconds = 1, ExcludedDirectories = "a/b" })
            .Select(failure => failure.MemberNames.Single())
            .Should().Equal(nameof(WorkerSettings.ExcludedDirectories), nameof(WorkerSettings.WorkLeaseSeconds));

    // ==========================================================================
    // FfmpegPath
    // ==========================================================================

    [Fact]
    public void Validate_FfmpegPathEmpty_ReturnsFailed()
    {
        IsValid(ValidSettings with { FfmpegPath = string.Empty }).Should().BeFalse();
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
        settings.ExcludedDirectories.Should().BeEmpty();
        settings.FfmpegPath.Should().Be("ffmpeg");
    }
}
