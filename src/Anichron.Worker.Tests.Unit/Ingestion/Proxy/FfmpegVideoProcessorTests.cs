using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class FfmpegVideoProcessorTests
{
    private sealed class TestFixture
    {
        public IProcessLauncher Launcher { get; } = Substitute.For<IProcessLauncher>();

        public int VideoShortEdge { get; init; } = 720;

        public FfmpegVideoProcessor Build()
            => new(
                Launcher,
                Options.Create(new WorkerSettings { FfmpegPath = "ffmpeg", VideoShortEdge = VideoShortEdge, VideoBitrateKbps = 4000 }),
                Substitute.For<ILogger<FfmpegVideoProcessor>>());
    }

    private static Task<ProcessResult> ProbeResultAsync(bool success)
        => Task.FromResult(new ProcessResult(success ? 0 : 1, string.Empty));

    private static bool IsProbeCall(IReadOnlyList<string> arguments) => arguments.Contains("/dev/null");

    // ==========================================================================
    // TranscodeAsync
    // ==========================================================================

    [Fact]
    public async Task TranscodeAsync_WhenFirstEncoderProbeSucceeds_UsesFirstEncoderForTranscodeAsync()
    {
        var fixture = new TestFixture();
        fixture.Launcher
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProbeResultAsync(true));

        await fixture.Build().TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);

        await fixture.Launcher.Received(1).RunAsync(
            "ffmpeg",
            Arg.Is<IReadOnlyList<string>>(a => a.Contains(H264Encoder.QuickSync) && !IsProbeCall(a)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TranscodeAsync_WhenFirstEncoderProbeFails_TriesNextEncoderAsync()
    {
        var fixture = new TestFixture();
        fixture.Launcher
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var arguments = callInfo.ArgAt<IReadOnlyList<string>>(1);
                if (IsProbeCall(arguments) && arguments.Contains(H264Encoder.QuickSync))
                    return ProbeResultAsync(false);
                return ProbeResultAsync(true);
            });

        await fixture.Build().TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);

        await fixture.Launcher.Received(1).RunAsync(
            "ffmpeg",
            Arg.Is<IReadOnlyList<string>>(a => a.Contains(H264Encoder.Nvenc) && !IsProbeCall(a)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TranscodeAsync_WhenAllGpuEncodersFail_FallsBackToLibx264Async()
    {
        var fixture = new TestFixture();
        fixture.Launcher
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var arguments = callInfo.ArgAt<IReadOnlyList<string>>(1);
                return ProbeResultAsync(!IsProbeCall(arguments));
            });

        await fixture.Build().TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);

        await fixture.Launcher.Received(1).RunAsync(
            "ffmpeg",
            Arg.Is<IReadOnlyList<string>>(a => a.Contains(H264Encoder.Software) && !IsProbeCall(a)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TranscodeAsync_DetectedEncoderIsCached_DoesNotReprobeOnSubsequentCallsAsync()
    {
        var fixture = new TestFixture();
        fixture.Launcher
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProbeResultAsync(true));
        var processor = fixture.Build();

        await processor.TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);
        var second = processor.TranscodeAsync("/src/video2.mp4", "/out/video2.mp4", CancellationToken.None);

        second.IsCompleted.Should().BeTrue("the first call released the encoder lock");
        await second;

        await fixture.Launcher.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(a => IsProbeCall(a)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TranscodeAsync_WhenTranscodeExitCodeNonZero_ThrowsFfmpegExceptionAsync()
    {
        var fixture = new TestFixture();
        fixture.Launcher
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var arguments = callInfo.ArgAt<IReadOnlyList<string>>(1);
                return ProbeResultAsync(IsProbeCall(arguments));
            });

        var act = () => fixture.Build()
            .TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);

        await act.Should().ThrowAsync<FfmpegException>();
    }

    [Fact]
    public async Task TranscodeAsync_PassesSourceAndOutputPathsToLauncherAsync()
    {
        var fixture = new TestFixture();
        fixture.Launcher
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProbeResultAsync(true));

        await fixture.Build().TranscodeAsync("/nas/source.mp4", "/proxies/output.mp4", CancellationToken.None);

        await fixture.Launcher.Received(1).RunAsync(
            "ffmpeg",
            Arg.Is<IReadOnlyList<string>>(a => a.Contains("/nas/source.mp4") && a.Contains("/proxies/output.mp4") && !IsProbeCall(a)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TranscodeAsync_AppliesVideoSettingsFromOptionsToFfmpegArgumentsAsync()
    {
        var fixture = new TestFixture { VideoShortEdge = 540 };
        fixture.Launcher
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProbeResultAsync(true));

        await fixture.Build().TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);

        await fixture.Launcher.Received(1).RunAsync(
            "ffmpeg",
            Arg.Is<IReadOnlyList<string>>(a =>
                a.Contains("scale='if(gt(iw,ih),-2,min(540,iw))':'if(gt(iw,ih),min(540,ih),-2)'")
                && a.Contains("4000k")
                && !IsProbeCall(a)),
            Arg.Any<CancellationToken>());
    }

    private static List<string[]> CaptureCalls(TestFixture fixture, int transcodeExitCode = 0)
    {
        var calls = new List<string[]>();
        fixture.Launcher
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var arguments = callInfo.ArgAt<IReadOnlyList<string>>(1).ToArray();
                calls.Add(arguments);
                return Task.FromResult(new ProcessResult(IsProbeCall(arguments) ? 0 : transcodeExitCode, "boom\n"));
            });
        return calls;
    }

    [Fact]
    public async Task TranscodeAsync_ProbesTheEncoderWithATinySyntheticClipAsync()
    {
        var fixture = new TestFixture();
        var calls = CaptureCalls(fixture);

        await fixture.Build().TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);

        calls[0].Should().Equal(
            "-f", "lavfi",
            "-i", "color=black:s=64x64:r=1:d=0.1",
            "-c:v", H264Encoder.QuickSync,
            "-f", "mp4",
            "/dev/null",
            "-y");
    }

    [Fact]
    public async Task TranscodeAsync_TranscodesToBrowserCompatibleMp4CappedOnTheShortEdgeAsync()
    {
        var fixture = new TestFixture();
        var calls = CaptureCalls(fixture);

        await fixture.Build().TranscodeAsync("/src/video.mp4", "/out/video_720p.mp4.0123abcd.tmp", CancellationToken.None);

        calls[^1].Should().Equal(
            "-hide_banner",
            "-loglevel", "error",
            "-i", "/src/video.mp4",
            "-vf", "scale='if(gt(iw,ih),-2,min(720,iw))':'if(gt(iw,ih),min(720,ih),-2)'",
            "-c:v", H264Encoder.QuickSync,
            "-pix_fmt", "yuv420p",
            "-profile:v", "high",
            "-level:v", "4.0",
            "-b:v", "4000k",
            "-c:a", "aac",
            "-b:a", "128k",
            "-movflags", "+faststart",
            "-f", "mp4",
            "-y",
            "/out/video_720p.mp4.0123abcd.tmp");
    }

    [Fact]
    public async Task TranscodeAsync_TranscodeFails_ExceptionStartsWithTheErrorThenTheSpaceJoinedCommandAsync()
    {
        var fixture = new TestFixture();
        CaptureCalls(fixture, transcodeExitCode: 3);

        var act = () => fixture.Build().TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<FfmpegException>()).Which;
        ex.ExitCode.Should().Be(3);
        ex.Stderr.Should().Be("boom\n");
        ex.Message.Should().StartWith("boom\nFFmpeg exited with code 3: ffmpeg -hide_banner -loglevel error -i /src/video.mp4 -vf ");
    }

    // ==========================================================================
    // FfmpegException
    // ==========================================================================

    [Fact]
    public void FfmpegException_FourParamCtor_SetsExitCode()
    {
        var ex = new FfmpegException("ffmpeg", "-i source.mp4", 1, "error output");

        ex.ExitCode.Should().Be(1);
    }

    [Fact]
    public void FfmpegException_FourParamCtor_SetsStderr()
    {
        var ex = new FfmpegException("ffmpeg", "-i source.mp4", 1, "error output");

        ex.Stderr.Should().Be("error output");
    }

    [Fact]
    public void FfmpegException_FourParamCtor_MessageContainsExitCodeAndArgs()
    {
        var ex = new FfmpegException("ffmpeg", "-i source.mp4", 2, "some error");

        ex.Message.Should().Contain("2").And.Contain("-i source.mp4");
    }

    [Fact]
    public void FfmpegException_ParameterlessCtor_HasEmptyStderr()
    {
        new FfmpegException().Stderr.Should().BeEmpty();
    }

    // ==========================================================================
    // Dispose
    // ==========================================================================

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var processor = new TestFixture().Build();

        var act = processor.Dispose;

        act.Should().NotThrow();
    }
}
