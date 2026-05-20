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

public sealed class VideoProxyMiddlewareTests
{
    private sealed class TestFixture
    {
        public IVideoProxyGenerator Generator { get; } = Substitute.For<IVideoProxyGenerator>();
        public MockFileSystem FileSystem { get; }
        public Instant Now { get; } = Instant.FromUtc(2026, 5, 20, 12, 0, 0);
        public IClock Clock { get; }
        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();

        public TestFixture()
        {
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            Clock = clock;

            Generator.FileName.Returns("video_720p.mp4");
            Generator.ProxyType.Returns(ProxyType.WebVideo);

            FileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                ["/nas/video.mp4"] = new MockFileData([0x00, 0x01]),
            });

            Generator.TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var outputPath = callInfo.ArgAt<string>(1);
                    FileSystem.File.WriteAllBytes(outputPath, [0xAA, 0xBB, 0xCC, 0xDD]);
                    return Task.CompletedTask;
                });
        }

        public VideoProxyMiddleware Build()
            => new([Generator],
                   new TwoLevelHexShardStrategy(),
                   Options.Create(new WorkerSettings { ProxyPath = "/proxies" }),
                   FileSystem,
                   Clock,
                   GuidFactory,
                   Substitute.For<ILogger<VideoProxyMiddleware>>());
    }

    private static IngestionContext MakeContext(MediaType mediaType = MediaType.Video)
        => new()
        {
            Item = new SingleFileItem("/nas/video.mp4", "video.mp4", mediaType),
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
    public async Task InvokeAsync_VideoItem_AddsOneProxyFileToContextAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_AddsWebVideoProxyAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().ContainSingle(p => p.ProxyType == ProxyType.WebVideo);
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_ProxyPathContainsAssetShardAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var expectedShard = context.AssetId.ToString("N")[..2];
        context.ProxyFiles.Should().AllSatisfy(p => p.ProxyPath.Should().StartWith(expectedShard));
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_ProxyFileHasContextAssetIdAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().AllSatisfy(p => p.AssetId.Should().Be(context.AssetId));
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_ProxyFileHasCreatedAtFromClockAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().AllSatisfy(p => p.CreatedAt.Should().Be(fixture.Now));
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_InvokesGeneratorTranscodeAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await fixture.Generator.Received(1)
            .TranscodeAsync(context.Item.AbsolutePath, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_SetsFileSizeFromOutputFileAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        // Generator writes 4 bytes ([0xAA, 0xBB, 0xCC, 0xDD])
        context.ProxyFiles.Single().SizeBytes.Should().Be(4);
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_WritesOutputFileToDiskAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var shard = context.AssetId.ToString("N");
        fixture.FileSystem.FileExists($"/proxies/{shard[..2]}/{shard[2..]}/video_720p.mp4")
            .Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_CreatesOutputDirectoryAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        var shard = context.AssetId.ToString("N");
        fixture.FileSystem.Directory.Exists($"/proxies/{shard[..2]}/{shard[2..]}")
            .Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_VideoItem_AssignsProxyFileIdsFromGuidFactoryAsync()
    {
        var fixture = new TestFixture();
        var expectedId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        fixture.GuidFactory.NewGuid().Returns(expectedId);
        var context = MakeContext();

        await fixture.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().AllSatisfy(p => p.Id.Should().Be(expectedId));
    }

    [Fact]
    public async Task InvokeAsync_MultipleGenerators_AddsOneProxyFilePerGeneratorAsync()
    {
        var fixture = new TestFixture();

        var secondGenerator = Substitute.For<IVideoProxyGenerator>();
        secondGenerator.FileName.Returns("video_1080p.mp4");
        secondGenerator.ProxyType.Returns(ProxyType.WebVideo);
        secondGenerator.TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                fixture.FileSystem.File.WriteAllBytes(callInfo.ArgAt<string>(1), [0x01]);
                return Task.CompletedTask;
            });

        var middleware = new VideoProxyMiddleware(
            [fixture.Generator, secondGenerator],
            new TwoLevelHexShardStrategy(),
            Options.Create(new WorkerSettings { ProxyPath = "/proxies" }),
            fixture.FileSystem,
            fixture.Clock,
            fixture.GuidFactory,
            Substitute.For<ILogger<VideoProxyMiddleware>>());

        var context = MakeContext();
        await middleware.InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.ProxyFiles.Should().HaveCount(2);
        await fixture.Generator.Received(1).TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await secondGenerator.Received(1).TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // CanInvoke
    // ==========================================================================

    [Fact]
    public void CanInvoke_VideoItem_ReturnsTrue()
    {
        new TestFixture().Build().CanInvoke(MakeContext(MediaType.Video)).Should().BeTrue();
    }

    [Fact]
    public void CanInvoke_ImageItem_ReturnsFalse()
    {
        new TestFixture().Build().CanInvoke(MakeContext(MediaType.Image)).Should().BeFalse();
    }

    [Fact]
    public void CanInvoke_LivePhotoPairItem_ReturnsFalse()
    {
        var context = new IngestionContext
        {
            Item = new LivePhotoPairItem("/nas/photo.heic", "photo.heic", "/nas/photo.mov", "photo.mov"),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" },
            AssetId = Guid.NewGuid(),
        };
        new TestFixture().Build().CanInvoke(context).Should().BeFalse();
    }
}
