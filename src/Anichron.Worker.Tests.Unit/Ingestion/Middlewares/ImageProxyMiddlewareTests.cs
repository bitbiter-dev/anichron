using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class ImageProxyMiddlewareTests
{
    private sealed class TestFixture
    {
        internal static readonly byte[] MinimalPng = CreateMinimalPng();

        private static byte[] CreateMinimalPng()
        {
            using var img = new Image<Rgb24>(1, 1);
            using var ms = new MemoryStream();
            img.SaveAsPng(ms);
            return ms.ToArray();
        }

        public IImageProcessor ImageProcessor { get; } = Substitute.For<IImageProcessor>();
        public MockFileSystem FileSystem { get; } = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/nas/photo.jpg"] = new MockFileData(MinimalPng),
        });
        public Instant Now { get; } = Instant.FromUtc(2026, 5, 19, 10, 0, 0);

        public TestFixture()
        {
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            Clock = clock;

            ImageProcessor.EncodeThumbnailAsync(Arg.Any<Image<Rgb24>>(), Arg.Any<CancellationToken>())
                .Returns([0x01, 0x02]);
            ImageProcessor.EncodeFullPreviewAsync(Arg.Any<Image<Rgb24>>(), Arg.Any<CancellationToken>())
                .Returns([0x03, 0x04]);
            ImageProcessor.ComputeBlurhashAsync(Arg.Any<Image<Rgb24>>(), Arg.Any<CancellationToken>())
                .Returns("LGFFaXYk^6#M@-5c,1J5@[or[Q6.");

            DirectoryStrategy.GetDirectory(Arg.Any<Guid>(), Arg.Any<string>()).Returns("ab/cd");
        }

        public IClock Clock { get; }
        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();
        public IProxyDirectoryStrategy DirectoryStrategy { get; } = Substitute.For<IProxyDirectoryStrategy>();

        public ImageProxyMiddleware Build(IImageProcessor? imageProcessor = null, WorkerSettings? settings = null)
        {
            imageProcessor ??= ImageProcessor;
            var options = Options.Create(settings ?? new WorkerSettings());
            return Build(
                [new ThumbnailGenerator(imageProcessor, options), new FullPreviewGenerator(imageProcessor, options)],
                new BlurhashEncoder(imageProcessor, options));
        }

        public ImageProxyMiddleware Build(IEnumerable<IImageProxyGenerator> generators, BlurhashEncoder? blurhashEncoder = null)
        {
            return new(generators,
                       blurhashEncoder ?? new BlurhashEncoder(ImageProcessor, Options.Create(new WorkerSettings())),
                       DirectoryStrategy,
                       new ProxyStagingWriter(
                           FileSystem,
                           Clock,
                           GuidFactory,
                           Substitute.For<ILogger<ProxyStagingWriter>>()),
                       FileSystem,
                       Substitute.For<ILogger<ImageProxyMiddleware>>());
        }
    }

    private static IngestionContext MakeContext(MediaType mediaType = MediaType.Image)
        => new()
        {
            Item = new SingleFileItem("/nas/photo.jpg", "photo.jpg", mediaType),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" },
            AttemptId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            ContentHash = "0123456789abcdef",
            SecondaryHash = "fedcba9876543210",
        };

    private static Task NoOpNextAsync(IngestionContext ctx, CancellationToken ct)
    {
        _ = (ctx, ct);
        return Task.CompletedTask;
    }

    // ==========================================================================
    // InvokeAsync
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_Always_CallsNextAsync()
    {
        var fixture = new TestFixture();
        var nextCalled = false;
        Task NextAsync(IngestionContext ctx, CancellationToken ct)
        {
            _ = (ctx, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await fixture.Build().InvokeAsync(MakeContext(), NextAsync, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_AddsTwoProxyFilesToContextAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_AddsThumbnailProxyAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().ContainSingle(p => p.ProxyType == ProxyType.Thumbnail);
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_AddsFullPreviewProxyAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().ContainSingle(p => p.ProxyType == ProxyType.FullPreview);
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_PutsTheBlurhashOnTheContextAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Blurhash.Should().Be("LGFFaXYk^6#M@-5c,1J5@[or[Q6.");
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_ProxyPathsStartWithStrategyDirectoryAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().AllSatisfy(p => p.ProxyPath.Should().StartWith("ab/cd"));
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_ProxyFilesHaveContextAssetIdAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().AllSatisfy(p => p.AssetId.Should().Be(context.AssetId));
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_ProxyFilesHaveCreatedAtFromClockAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().AllSatisfy(p => p.CreatedAt.Should().Be(fixture.Now));
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesThumbnailFileToDiskAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        fixture.FileSystem.FileExists("/data/proxies/ab/cd/thumbnail.jpg").Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesPreviewFileToDiskAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        fixture.FileSystem.FileExists("/data/proxies/ab/cd/preview.jpg").Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesOnlyTheThumbnailAndPreviewFilesAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        fixture.FileSystem.AllFiles.Where(path => path.StartsWith("/data/proxies/", StringComparison.Ordinal))
            .Should().BeEquivalentTo("/data/proxies/ab/cd/thumbnail.jpg", "/data/proxies/ab/cd/preview.jpg");
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_DerivesDirectoryFromConfigIdAndContentHashAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        // Not the asset id, and not the Live Photo secondary hash ("fedcba9876543210").
        fixture.DirectoryStrategy.Received(1).GetDirectory(context.Config.Id, "0123456789abcdef");
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_LeavesNoTemporaryFilesOnDiskAsync()
    {
        var fixture = new TestFixture();

        await fixture.Build().InvokeAsync(MakeContext(), NoOpNextAsync, CancellationToken.None);

        fixture.FileSystem.AllFiles.Should().NotContain(path => path.EndsWith(".tmp"));
    }

    [Fact]
    public async Task InvokeAsync_GeneratorThrows_DropsTemporaryFileAsync()
    {
        var fixture = new TestFixture();
        fixture.ImageProcessor.EncodeThumbnailAsync(Arg.Any<Image<Rgb24>>(), Arg.Any<CancellationToken>())
            .Returns<byte[]>(_ => throw new InvalidOperationException("boom"));

        var act = () => fixture.Build().InvokeAsync(MakeContext(), NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.FileSystem.AllFiles.Should().NotContain(path => path.EndsWith(".tmp"));
    }

    // ==========================================================================
    // Downscale chain
    // ==========================================================================

    private const int SourceWidth = 160;
    private const int SourceHeight = 80;

    private sealed class RecordingGenerator(
        string fileName, ProxyBound bound, List<string> events, List<(string Name, Size Size, Corner Marker)> received)
        : IImageProxyGenerator
    {
        public string FileName => fileName;
        public ProxyType ProxyType => ProxyType.Thumbnail;
        public ProxyBound Bound => bound;

        public async Task<byte[]> GenerateAsync(Image<Rgb24> image, CancellationToken ct)
        {
            events.Add($"start {fileName}");
            received.Add((fileName, image.Size, MarkedCorner(image)));
            image.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                    rows.GetRowSpan(y).Fill(Color.Blue.ToPixel<Rgb24>());
            });
            await Task.Yield();
            events.Add($"end {fileName}");
            return [0x01];
        }
    }

    [Fact]
    public async Task InvokeAsync_Generators_RunOneAtATimeLargestFirstEachOnItsOwnShrunkCopyAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/photo.jpg", new MockFileData(MarkedJpegWithOrientation(ExifOrientationMode.TopLeft)));
        var events = new List<string>();
        var received = new List<(string Name, Size Size, Corner Marker)>();
        IImageProxyGenerator[] generators =
        [
            new RecordingGenerator("small", new ProxyBound(ProxyEdge.LongEdge, 8), events, received),
            new RecordingGenerator("large", new ProxyBound(ProxyEdge.LongEdge, 60), events, received),
            new RecordingGenerator("medium", new ProxyBound(ProxyEdge.ShortEdge, 10), events, received),
        ];

        await fixture.Build(generators).InvokeAsync(MakeContext(), NoOpNextAsync, CancellationToken.None);

        events.Should().Equal(
            "start large", "end large", "start medium", "end medium", "start small", "end small");
        received.Should().Equal(
            ("large", new Size(60, 30), Corner.TopLeft),
            ("medium", new Size(20, 10), Corner.TopLeft),
            ("small", new Size(8, 4), Corner.TopLeft));
    }

    [Fact]
    public async Task InvokeAsync_Blurhash_TakesItsTurnInTheLargestFirstChainOnItsOwnCopyAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/photo.jpg", new MockFileData(MarkedJpegWithOrientation(ExifOrientationMode.TopLeft)));
        var events = new List<string>();
        var received = new List<(string Name, Size Size, Corner Marker)>();
        fixture.ImageProcessor.ComputeBlurhashAsync(Arg.Any<Image<Rgb24>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var image = call.Arg<Image<Rgb24>>();
                events.Add("blurhash");
                received.Add(("blurhash", image.Size, MarkedCorner(image)));
                image.Mutate(x => x.Flip(FlipMode.Horizontal));
                return "hash";
            });
        IImageProxyGenerator[] generators =
        [
            new RecordingGenerator("small", new ProxyBound(ProxyEdge.LongEdge, 8), events, received),
            new RecordingGenerator("large", new ProxyBound(ProxyEdge.LongEdge, 60), events, received),
        ];
        var encoder = new BlurhashEncoder(
            fixture.ImageProcessor, Options.Create(new WorkerSettings { BlurhashSampleLongEdge = 20 }));

        await fixture.Build(generators, encoder).InvokeAsync(MakeContext(), NoOpNextAsync, CancellationToken.None);

        events.Should().Equal("start large", "end large", "blurhash", "start small", "end small");
        received.Should().Equal(
            ("large", new Size(60, 30), Corner.TopLeft),
            ("blurhash", new Size(20, 10), Corner.TopLeft),
            ("small", new Size(8, 4), Corner.TopLeft));
    }

    // ==========================================================================
    // Sizing and orientation (real processor)
    // ==========================================================================

    public enum Corner { TopLeft = 0, TopRight = 1, BottomRight = 2, BottomLeft = 3 }

    private static readonly WorkerSettings SmallProxySettings = new() { PreviewLongEdge = 100, ThumbnailShortEdge = 40 };

    public static TheoryData<string, ushort, int, int, Corner> OrientedProxies()
    {
        (ushort Orientation, bool Portrait, Corner Marker)[] orientations =
        [
            (ExifOrientationMode.TopLeft, false, Corner.TopLeft),
            (ExifOrientationMode.TopRight, false, Corner.TopRight),
            (ExifOrientationMode.BottomRight, false, Corner.BottomRight),
            (ExifOrientationMode.BottomLeft, false, Corner.BottomLeft),
            (ExifOrientationMode.LeftTop, true, Corner.TopLeft),
            (ExifOrientationMode.RightTop, true, Corner.TopRight),
            (ExifOrientationMode.RightBottom, true, Corner.BottomRight),
            (ExifOrientationMode.LeftBottom, true, Corner.BottomLeft),
        ];
        var data = new TheoryData<string, ushort, int, int, Corner>();
        foreach (var (orientation, portrait, marker) in orientations)
        {
            data.Add("thumbnail.jpg", orientation, portrait ? 40 : 80, portrait ? 80 : 40, marker);
            data.Add("preview.jpg", orientation, portrait ? 50 : 100, portrait ? 100 : 50, marker);
        }

        return data;
    }

    private static byte[] MarkedJpegWithOrientation(ushort orientation, int width = SourceWidth, int height = SourceHeight)
    {
        using var img = new Image<Rgb24>(width, height, Color.Black.ToPixel<Rgb24>());
        img.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < height / 2; y++)
                rows.GetRowSpan(y)[..(width / 2)].Fill(Color.Red.ToPixel<Rgb24>());
        });
        img.Metadata.ExifProfile = new ExifProfile();
        img.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static Corner MarkedCorner(Image<Rgb24> image)
    {
        var (left, right) = (image.Width / 4, image.Width * 3 / 4);
        var (top, bottom) = (image.Height / 4, image.Height * 3 / 4);
        (Corner Corner, int X, int Y)[] centres =
        [
            (Corner.TopLeft, left, top),
            (Corner.TopRight, right, top),
            (Corner.BottomRight, right, bottom),
            (Corner.BottomLeft, left, bottom),
        ];
        return centres.Single(c => image[c.X, c.Y].R > 128).Corner;
    }

    private static async Task<Image<Rgb24>> GenerateWithRealProcessorAsync(
        byte[] source, WorkerSettings settings, string proxyFileName)
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/photo.jpg", new MockFileData(source));
        var processor = new ImageSharpProcessor(Options.Create(settings));

        await fixture.Build(processor, settings).InvokeAsync(MakeContext(), NoOpNextAsync, CancellationToken.None);

        var bytes = await fixture.FileSystem.File.ReadAllBytesAsync(
            $"/data/proxies/ab/cd/{proxyFileName}", TestContext.Current.CancellationToken);
        return Image.Load<Rgb24>(bytes);
    }

    [Theory]
    [MemberData(nameof(OrientedProxies))]
    public async Task InvokeAsync_OrientedJpeg_WritesUprightProxyBoundedOnItsEdgeAsync(
        string proxyFileName, ushort orientation, int expectedWidth, int expectedHeight, Corner expectedMarker)
    {
        using var proxy = await GenerateWithRealProcessorAsync(
            MarkedJpegWithOrientation(orientation), SmallProxySettings, proxyFileName);

        proxy.Size.Should().Be(new Size(expectedWidth, expectedHeight));
        MarkedCorner(proxy).Should().Be(expectedMarker);
        var tag = proxy.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var value) == true
            ? value.Value
            : ExifOrientationMode.TopLeft;
        tag.Should().Be(ExifOrientationMode.TopLeft);
    }

    [Theory]
    [InlineData("thumbnail.jpg", 120, 60)]
    [InlineData("preview.jpg", 100, 50)]
    public async Task InvokeAsync_ThumbnailLargerThanPreview_StillSizesEachFromEnoughPixelsAsync(
        string proxyFileName, int expectedWidth, int expectedHeight)
    {
        var settings = new WorkerSettings { PreviewLongEdge = 100, ThumbnailShortEdge = 60 };

        using var proxy = await GenerateWithRealProcessorAsync(
            MarkedJpegWithOrientation(ExifOrientationMode.TopLeft), settings, proxyFileName);

        proxy.Size.Should().Be(new Size(expectedWidth, expectedHeight));
    }

    [Theory]
    [InlineData("thumbnail.jpg")]
    [InlineData("preview.jpg")]
    public async Task InvokeAsync_SourceSmallerThanBounds_KeepsSourceSizeAsync(string proxyFileName)
    {
        using var proxy = await GenerateWithRealProcessorAsync(
            MarkedJpegWithOrientation(ExifOrientationMode.TopLeft, 32, 16), new WorkerSettings(), proxyFileName);

        proxy.Size.Should().Be(new Size(32, 16));
    }

    // ==========================================================================
    // CanInvoke
    // ==========================================================================

    [Fact]
    public void CanInvoke_ImageItem_ReturnsTrue()
    {
        new TestFixture().Build().CanInvoke(MakeContext(MediaType.Image)).Should().BeTrue();
    }

    [Fact]
    public void CanInvoke_LivePhotoItem_ReturnsTrue()
    {
        var context = new IngestionContext
        {
            Item = new LivePhotoPairItem("/nas/photo.heic", "photo.heic", "/nas/photo.mov", "photo.mov"),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" },
            AttemptId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            ContentHash = "0123456789abcdef",
            SecondaryHash = "fedcba9876543210",
        };
        new TestFixture().Build().CanInvoke(context).Should().BeTrue();
    }

    [Fact]
    public void CanInvoke_VideoItem_ReturnsFalse()
    {
        new TestFixture().Build().CanInvoke(MakeContext(MediaType.Video)).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairItem_GeneratesProxyFilesAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/photo.heic", new MockFileData(TestFixture.MinimalPng));
        var context = new IngestionContext
        {
            Item = new LivePhotoPairItem("/nas/photo.heic", "photo.heic", "/nas/photo.mov", "photo.mov"),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" },
            AttemptId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            ContentHash = "0123456789abcdef",
            SecondaryHash = "fedcba9876543210",
        };

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().HaveCount(2);
        context.Blurhash.Should().Be("LGFFaXYk^6#M@-5c,1J5@[or[Q6.");
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_SetsCorrectSizeBytesOnEachProxyFileAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Single(p => p.ProxyType == ProxyType.Thumbnail).SizeBytes.Should().Be(2);
        context.ProxyFiles.Single(p => p.ProxyType == ProxyType.FullPreview).SizeBytes.Should().Be(2);
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_AssignsProxyFileIdsFromGuidFactoryAsync()
    {
        var fixture = new TestFixture();
        var expectedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        fixture.GuidFactory.NewGuid().Returns(expectedId);
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().AllSatisfy(p => p.Id.Should().Be(expectedId));
    }
}
