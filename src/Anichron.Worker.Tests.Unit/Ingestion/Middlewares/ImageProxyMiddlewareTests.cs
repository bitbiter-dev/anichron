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
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class ImageProxyMiddlewareTests
{
    private sealed class TestFixture
    {
        internal static readonly byte[] MinimalPng = CreateMinimalPng();

        private static byte[] CreateMinimalPng()
        {
            using var img = new Image<Rgba32>(1, 1);
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

            ImageProcessor.CreateThumbnailAsync(Arg.Any<Image<Rgba32>>(), Arg.Any<CancellationToken>())
                .Returns([0x01, 0x02]);
            ImageProcessor.CreateFullPreviewAsync(Arg.Any<Image<Rgba32>>(), Arg.Any<CancellationToken>())
                .Returns([0x03, 0x04]);
            ImageProcessor.ComputeBlurhashAsync(Arg.Any<Image<Rgba32>>(), Arg.Any<CancellationToken>())
                .Returns("LGFFaXYk^6#M@-5c,1J5@[or[Q6.");

            DirectoryStrategy.GetDirectory(Arg.Any<Guid>(), Arg.Any<string>()).Returns("ab/cd");
        }

        public IClock Clock { get; }
        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();
        public IProxyDirectoryStrategy DirectoryStrategy { get; } = Substitute.For<IProxyDirectoryStrategy>();

        public ImageProxyMiddleware Build(IImageProcessor? imageProcessor = null)
        {
            imageProcessor ??= ImageProcessor;
            IImageProxyGenerator[] generators =
            [
                new ThumbnailGenerator(imageProcessor),
                new FullPreviewGenerator(imageProcessor),
                new BlurhashGenerator(imageProcessor),
            ];
            return new(generators,
                       DirectoryStrategy,
                       new ProxyStagingWriter(
                           FileSystem,
                           Options.Create(new WorkerSettings { ProxyPath = "/proxies" }),
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
    public async Task InvokeAsync_ImageItem_AddsThreeProxyFilesToContextAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().HaveCount(3);
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
    public async Task InvokeAsync_ImageItem_AddsBlurhashProxyAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().ContainSingle(p => p.ProxyType == ProxyType.BlurHash);
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

        fixture.FileSystem.FileExists("/proxies/ab/cd/thumbnail.jpg").Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesPreviewFileToDiskAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        fixture.FileSystem.FileExists("/proxies/ab/cd/preview.jpg").Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesBlurhashFileToDiskAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        fixture.FileSystem.FileExists("/proxies/ab/cd/blurhash.txt").Should().BeTrue();
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
        fixture.ImageProcessor.CreateThumbnailAsync(Arg.Any<Image<Rgba32>>(), Arg.Any<CancellationToken>())
            .Returns<byte[]>(_ => throw new InvalidOperationException("boom"));

        var act = () => fixture.Build().InvokeAsync(MakeContext(), NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.FileSystem.AllFiles.Should().NotContain(path => path.EndsWith(".tmp"));
    }

    // ==========================================================================
    // Orientation
    // ==========================================================================

    public enum Corner { TopLeft = 0, TopRight = 1, BottomRight = 2, BottomLeft = 3 }

    private const int SourceWidth = 80;
    private const int SourceHeight = 40;
    private const int ThumbnailMaxWidth = 40;

    public static TheoryData<string, ushort, int, int, Corner> OrientedProxies()
    {
        (ushort Orientation, bool Swapped, Corner Marker)[] orientations =
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
        foreach (var (orientation, swapped, marker) in orientations)
        {
            data.Add("thumbnail.jpg", orientation, ThumbnailMaxWidth, swapped ? ThumbnailMaxWidth * 2 : ThumbnailMaxWidth / 2, marker);
            data.Add("preview.jpg", orientation, swapped ? SourceHeight : SourceWidth, swapped ? SourceWidth : SourceHeight, marker);
        }

        return data;
    }

    private static byte[] MarkedJpegWithOrientation(ushort orientation)
    {
        using var img = new Image<Rgba32>(SourceWidth, SourceHeight, Color.Black.ToPixel<Rgba32>());
        img.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < SourceHeight / 2; y++)
                rows.GetRowSpan(y)[..(SourceWidth / 2)].Fill(Color.Red.ToPixel<Rgba32>());
        });
        img.Metadata.ExifProfile = new ExifProfile();
        img.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static Corner MarkedCorner(Image<Rgba32> image)
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

    [Theory]
    [MemberData(nameof(OrientedProxies))]
    public async Task InvokeAsync_OrientedJpeg_WritesUprightProxyWithoutRotationTagAsync(
        string proxyFileName, ushort orientation, int expectedWidth, int expectedHeight, Corner expectedMarker)
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/photo.jpg", new MockFileData(MarkedJpegWithOrientation(orientation)));
        var processor = new ImageSharpProcessor(
            Options.Create(new WorkerSettings { ThumbnailMaxWidth = ThumbnailMaxWidth }));

        await fixture.Build(processor).InvokeAsync(MakeContext(), NoOpNextAsync, CancellationToken.None);

        var bytes = await fixture.FileSystem.File.ReadAllBytesAsync(
            $"/proxies/ab/cd/{proxyFileName}", TestContext.Current.CancellationToken);
        using var proxy = Image.Load<Rgba32>(bytes);
        (proxy.Width, proxy.Height).Should().Be((expectedWidth, expectedHeight));
        MarkedCorner(proxy).Should().Be(expectedMarker);
        var tag = proxy.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var value) == true
            ? value.Value
            : ExifOrientationMode.TopLeft;
        tag.Should().Be(ExifOrientationMode.TopLeft);
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
            AssetId = Guid.NewGuid(),
            ContentHash = "0123456789abcdef",
            SecondaryHash = "fedcba9876543210",
        };

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().HaveCount(3);
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_SetsCorrectSizeBytesOnEachProxyFileAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        // Thumbnail [0x01,0x02] = 2 B; Preview [0x03,0x04] = 2 B; Blurhash = 28-char ASCII string = 28 B
        context.ProxyFiles.Single(p => p.ProxyType == ProxyType.Thumbnail).SizeBytes.Should().Be(2);
        context.ProxyFiles.Single(p => p.ProxyType == ProxyType.FullPreview).SizeBytes.Should().Be(2);
        context.ProxyFiles.Single(p => p.ProxyType == ProxyType.BlurHash).SizeBytes.Should().Be(28);
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
