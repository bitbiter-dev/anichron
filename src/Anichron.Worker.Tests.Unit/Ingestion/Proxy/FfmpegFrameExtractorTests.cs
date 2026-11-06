using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class FfmpegFrameExtractorTests
{
    private static readonly string[] FrameAtOneSecond =
    [
        "-hide_banner", "-loglevel", "error",
        "-ss", "1",
        "-i", "/nas/clip.mov",
        "-frames:v", "1", "-pix_fmt", "rgb24", "-c:v", "png", "-f", "image2pipe", "-",
    ];

    private static readonly string[] FirstFrame =
    [
        "-hide_banner", "-loglevel", "error",
        "-i", "/nas/clip.mov",
        "-frames:v", "1", "-pix_fmt", "rgb24", "-c:v", "png", "-f", "image2pipe", "-",
    ];

    private readonly IProcessLauncher launcher = Substitute.For<IProcessLauncher>();

    private FfmpegFrameExtractor Build()
        => new(launcher, Options.Create(new WorkerSettings { FfmpegPath = "/usr/bin/ffmpeg" }));

    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private void Answer(string[] arguments, ProcessResult result)
        => launcher.RunAsync("/usr/bin/ffmpeg", Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(arguments)), Arg.Any<CancellationToken>())
            .Returns(result);

    [Fact]
    public async Task ExtractAsync_VideoLongerThanASecond_ReturnsItsFrameAtOneSecondAsync()
    {
        Answer(FrameAtOneSecond, new ProcessResult(0, Png(6, 4), string.Empty));

        using var frame = await Build().ExtractAsync("/nas/clip.mov", TestContext.Current.CancellationToken);

        frame.Size.Should().Be(new Size(6, 4));
        await launcher.Received(1).RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_ClipShorterThanASecond_ReturnsItsFirstFrameAsync()
    {
        Answer(FrameAtOneSecond, new ProcessResult(0, [], string.Empty));
        Answer(FirstFrame, new ProcessResult(0, Png(3, 5), string.Empty));

        using var frame = await Build().ExtractAsync("/nas/clip.mov", TestContext.Current.CancellationToken);

        frame.Size.Should().Be(new Size(3, 5));
    }

    [Fact]
    public async Task ExtractAsync_FfmpegFails_ThrowsWithItsErrorWithoutRetryingAsync()
    {
        Answer(FrameAtOneSecond, new ProcessResult(1, [], "moov atom not found\n"));

        var act = () => Build().ExtractAsync("/nas/clip.mov", TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<FfmpegException>();
        thrown.Which.ExitCode.Should().Be(1);
        thrown.Which.Stderr.Should().Be("moov atom not found\n");
        thrown.Which.Message.Should().Be(
            "moov atom not found\n"
            + "FFmpeg exited with code 1: /usr/bin/ffmpeg -hide_banner -loglevel error -ss 1 -i /nas/clip.mov "
            + "-frames:v 1 -pix_fmt rgb24 -c:v png -f image2pipe -");
        await launcher.Received(1).RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_FirstFrameFails_ThrowsWithItsErrorAsync()
    {
        Answer(FrameAtOneSecond, new ProcessResult(0, [], string.Empty));
        Answer(FirstFrame, new ProcessResult(234, [], "Invalid data\n"));

        var act = () => Build().ExtractAsync("/nas/clip.mov", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<FfmpegException>()).Which.ExitCode.Should().Be(234);
    }

    [Fact]
    public async Task ExtractAsync_NoFrameAtAll_ThrowsNamingTheVideoAsync()
    {
        Answer(FrameAtOneSecond, new ProcessResult(0, [], string.Empty));
        Answer(FirstFrame, new ProcessResult(0, [], string.Empty));

        var act = () => Build().ExtractAsync("/nas/clip.mov", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FfmpegException>().WithMessage("FFmpeg read no frame from /nas/clip.mov.");
    }
}
