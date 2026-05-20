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

        public FfmpegVideoProcessor Build()
            => new(
                Launcher,
                Options.Create(new WorkerSettings { FfmpegPath = "ffmpeg", VideoMaxHeight = 720, VideoBitrateKbps = 4000 }),
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
        await processor.TranscodeAsync("/src/video2.mp4", "/out/video2.mp4", CancellationToken.None);

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
        var fixture = new TestFixture();
        fixture.Launcher
            .RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProbeResultAsync(true));

        await fixture.Build().TranscodeAsync("/src/video.mp4", "/out/video.mp4", CancellationToken.None);

        // TestFixture uses VideoMaxHeight=720, VideoBitrateKbps=4000 — verify both appear in the transcode call.
        await fixture.Launcher.Received(1).RunAsync(
            "ffmpeg",
            Arg.Is<IReadOnlyList<string>>(a => a.Any(arg => arg.Contains("720")) && a.Contains("4000k") && !IsProbeCall(a)),
            Arg.Any<CancellationToken>());
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
