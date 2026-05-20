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
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class ImageProxyMiddlewareTests
{
    private sealed class TestFixture
    {
        public IImageProcessor ImageProcessor { get; } = Substitute.For<IImageProcessor>();
        public MockFileSystem FileSystem { get; } = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/nas/photo.jpg"] = new MockFileData([0xFF, 0xD8, 0xFF, 0xD9]),
        });
        public Instant Now { get; } = Instant.FromUtc(2026, 5, 19, 10, 0, 0);

        public TestFixture()
        {
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            Clock = clock;

            ImageProcessor.CreateThumbnailAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns([0x01, 0x02]);
            ImageProcessor.CreateFullPreviewAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns([0x03, 0x04]);
            ImageProcessor.ComputeBlurhashAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns("LGFFaXYk^6#M@-5c,1J5@[or[Q6.");
        }

        public IClock Clock { get; }
        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();

        public ImageProxyMiddleware Build()
        {
            IImageProxyGenerator[] generators =
            [
                new ThumbnailGenerator(ImageProcessor),
                new FullPreviewGenerator(ImageProcessor),
                new BlurhashGenerator(ImageProcessor),
            ];
            return new(generators,
                       new TwoLevelHexShardStrategy(),
                       Options.Create(new WorkerSettings { ProxyPath = "/proxies" }),
                       FileSystem,
                       Clock,
                       GuidFactory,
                       Substitute.For<ILogger<ImageProxyMiddleware>>());
        }
    }

    private static IngestionContext MakeContext(MediaType mediaType = MediaType.Image)
        => new()
        {
            Item = new SingleFileItem("/nas/photo.jpg", "photo.jpg", mediaType),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" },
            AssetId = Guid.NewGuid(),
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
    public async Task InvokeAsync_ImageItem_ProxyPathsContainAssetShardAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var hex = context.AssetId.ToString("N");
        var expectedDirectory = $"{hex[..2]}/{hex[2..]}";
        context.ProxyFiles.Should().AllSatisfy(p => p.ProxyPath.Should().StartWith(expectedDirectory));
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

        var shard = context.AssetId.ToString("N");
        fixture.FileSystem.FileExists($"/proxies/{shard[..2]}/{shard[2..]}/thumbnail.jpg")
            .Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesPreviewFileToDiskAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var shard = context.AssetId.ToString("N");
        fixture.FileSystem.FileExists($"/proxies/{shard[..2]}/{shard[2..]}/preview.jpg")
            .Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesBlurhashFileToDiskAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var shard = context.AssetId.ToString("N");
        fixture.FileSystem.FileExists($"/proxies/{shard[..2]}/{shard[2..]}/blurhash.txt")
            .Should().BeTrue();
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
        fixture.FileSystem.AddFile("/nas/photo.heic", new MockFileData([0xFF, 0xD8, 0xFF, 0xD9]));
        var context = new IngestionContext
        {
            Item = new LivePhotoPairItem("/nas/photo.heic", "photo.heic", "/nas/photo.mov", "photo.mov"),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" },
            AssetId = Guid.NewGuid(),
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
