using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Proxy;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class UprightImageSourceTests
{
    private static readonly byte[] PhotoBytes = [0x01, 0x02, 0x03];

    private readonly IUprightImageDecoder decoder = Substitute.For<IUprightImageDecoder>();
    private readonly IVideoFrameExtractor frameExtractor = Substitute.For<IVideoFrameExtractor>();
    private readonly MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>
    {
        ["/nas/photo.jpg"] = new MockFileData(PhotoBytes),
        ["/nas/live.heic"] = new MockFileData(PhotoBytes),
    });

    private UprightImageSource Source() => new(decoder, frameExtractor, fileSystem);

    [Fact]
    public async Task LoadAsync_Photo_DecodesItsBytesAsync()
    {
        using var decoded = new Image<Rgb24>(3, 2);
        decoder.DecodeAsync(Arg.Is<byte[]>(b => b.SequenceEqual(PhotoBytes)), Arg.Any<CancellationToken>()).Returns(decoded);

        var image = await Source().LoadAsync(
            new SingleFileItem("/nas/photo.jpg", "photo.jpg", MediaType.Image), TestContext.Current.CancellationToken);

        image.Should().BeSameAs(decoded);
        await frameExtractor.DidNotReceiveWithAnyArgs().ExtractAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LoadAsync_LivePhoto_DecodesThePhotoAsync()
    {
        using var decoded = new Image<Rgb24>(3, 2);
        decoder.DecodeAsync(Arg.Is<byte[]>(b => b.SequenceEqual(PhotoBytes)), Arg.Any<CancellationToken>()).Returns(decoded);

        var image = await Source().LoadAsync(
            new LivePhotoPairItem("/nas/live.heic", "live.heic", "/nas/live.mov", "live.mov"),
            TestContext.Current.CancellationToken);

        image.Should().BeSameAs(decoded);
        await frameExtractor.DidNotReceiveWithAnyArgs().ExtractAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LoadAsync_Video_ExtractsItsFrameWithoutReadingTheFileAsync()
    {
        using var frame = new Image<Rgb24>(3, 2);
        frameExtractor.ExtractAsync("/nas/clip.mov", Arg.Any<CancellationToken>()).Returns(frame);

        var image = await Source().LoadAsync(
            new SingleFileItem("/nas/clip.mov", "clip.mov", MediaType.Video), TestContext.Current.CancellationToken);

        image.Should().BeSameAs(frame);
        await decoder.DidNotReceiveWithAnyArgs().DecodeAsync(default!, TestContext.Current.CancellationToken);
    }
}
