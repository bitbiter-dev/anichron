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
        var fx = new TestFixture();
        var nextCalled = false;
        Task NextAsync(IngestionContext ctx, CancellationToken ct)
        {
            _ = (ctx, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await fx.Build().InvokeAsync(MakeContext(), NextAsync, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_AddsThreeProxyFilesToContextAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().HaveCount(3);
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_AddsThumbnailProxyAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().ContainSingle(p => p.ProxyType == ProxyType.Thumbnail);
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_AddsFullPreviewProxyAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().ContainSingle(p => p.ProxyType == ProxyType.FullPreview);
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_AddsBlurhashProxyAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().ContainSingle(p => p.ProxyType == ProxyType.BlurHash);
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_ProxyPathsContainAssetShardAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var expectedShard = context.AssetId.ToString("N")[..2];
        context.ProxyFiles.Should().AllSatisfy(p => p.ProxyPath.Should().StartWith(expectedShard));
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_ProxyFilesHaveContextAssetIdAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().AllSatisfy(p => p.AssetId.Should().Be(context.AssetId));
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_ProxyFilesHaveCreatedAtFromClockAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().AllSatisfy(p => p.CreatedAt.Should().Be(fx.Now));
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesThumbnailFileToDiskAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var shard = context.AssetId.ToString("N");
        fx.FileSystem.FileExists($"/proxies/{shard[..2]}/{shard[2..]}/thumbnail.jpg")
            .Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesPreviewFileToDiskAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var shard = context.AssetId.ToString("N");
        fx.FileSystem.FileExists($"/proxies/{shard[..2]}/{shard[2..]}/preview.jpg")
            .Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ImageItem_WritesBlurhashFileToDiskAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var shard = context.AssetId.ToString("N");
        fx.FileSystem.FileExists($"/proxies/{shard[..2]}/{shard[2..]}/blurhash.txt")
            .Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_SkipsProxyGenerationAndCallsNextAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(MediaType.Video);
        fx.FileSystem.AddFile("/nas/photo.jpg", new MockFileData([0xFF, 0xD8]));

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().BeEmpty();
        await fx.ImageProcessor.DidNotReceive()
            .CreateThumbnailAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }
}
