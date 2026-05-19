using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class PersistenceMiddlewareTests
{
    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public MockFileSystem FileSystem { get; }
        public Instant Now { get; }

        public TestFixture()
        {
            Now = Instant.FromUtc(2026, 5, 19, 10, 0, 0);
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            Clock = clock;
            FileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                ["/abs/photo.jpg"] = new MockFileData([])
                {
                    LastWriteTime = new DateTimeOffset(2023, 6, 15, 12, 0, 0, TimeSpan.Zero),
                },
            });
        }

        public IClock Clock { get; }

        public PersistenceMiddleware Build()
            => new(Repository, UnitOfWork, FileSystem, Clock, Substitute.For<ILogger<PersistenceMiddleware>>());
    }

    private static readonly ExifData DefaultExif =
        new(100, 200, 0, new LocalDateTime(2023, 6, 15, 10, 0), null, null, null, null, null, null);

    private static IngestionContext MakeContext(
        IngestionItem? item = null,
        string? contentHash = "deadbeef",
        ExifData? exif = null) => new()
        {
            Item = item ?? new SingleFileItem("/abs/photo.jpg", "photo.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            ContentHash = contentHash,
            Exif = exif ?? DefaultExif,
        };

    private static Task NoOpNextAsync(IngestionContext ctx, CancellationToken ct)
    {
        _ = (ctx, ct);
        return Task.CompletedTask;
    }

    // ==========================================================================
    // CanInvoke
    // ==========================================================================

    [Fact]
    public void CanInvoke_WhenHashAndExifSet_ReturnsTrue()
    {
        new TestFixture().Build().CanInvoke(MakeContext()).Should().BeTrue();
    }

    [Fact]
    public void CanInvoke_WhenHashIsNull_ReturnsFalse()
    {
        new TestFixture().Build().CanInvoke(MakeContext(contentHash: null)).Should().BeFalse();
    }

    [Fact]
    public void CanInvoke_WhenExifIsNull_ReturnsFalse()
    {
        var context = new IngestionContext
        {
            Item = new SingleFileItem("/abs/photo.jpg", "photo.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            ContentHash = "deadbeef",
        };
        new TestFixture().Build().CanInvoke(context).Should().BeFalse();
    }

    // ==========================================================================
    // InvokeAsync
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_Always_AddsAssetToRepositoryAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        fx.Repository.Received(1).Add(Arg.Any<MediaAsset>());
    }

    [Fact]
    public async Task InvokeAsync_Always_CallsSaveChangesAsync()
    {
        var fx = new TestFixture();

        await fx.Build().InvokeAsync(MakeContext(), NoOpNextAsync, CancellationToken.None);

        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_Always_SetsContextAssetAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset.Should().NotBeNull();
    }

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
    public async Task InvokeAsync_WithExifDateCaptured_SetsMonthDayYearFromExifAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.Month.Should().Be(6);
        context.Asset!.Day.Should().Be(15);
        context.Asset!.Year.Should().Be(2023);
    }

    [Fact]
    public async Task InvokeAsync_WithNullDateCaptured_FallsBackToFileLastWriteTimeAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(exif: DefaultExif with { DateCaptured = null });

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.Year.Should().Be(2023);
        context.Asset!.Month.Should().Be(6);
        context.Asset!.Day.Should().Be(15);
    }

    [Fact]
    public async Task InvokeAsync_Always_SetsLastSeenOnNasFromClockAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.LastSeenOnNas.Should().Be(fx.Now);
    }

    [Fact]
    public async Task InvokeAsync_Always_SetsMetadataFromExifAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.Metadata!.Width.Should().Be(100);
        context.Asset!.Metadata!.Height.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_SingleFileItem_UsesItemMediaTypeAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(item: new SingleFileItem("/abs/video.mp4", "video.mp4", MediaType.Video));

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.MediaType.Should().Be(MediaType.Video);
    }

    [Fact]
    public async Task InvokeAsync_Always_SetsContentHashFromContextAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(contentHash: "deadbeef");

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.ContentHash.Should().Be("deadbeef");
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairItem_UsesImageMediaTypeAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/heic/photo.heic"] = new MockFileData([])
            {
                LastWriteTime = new DateTimeOffset(2023, 6, 15, 12, 0, 0, TimeSpan.Zero),
            },
        });
        var fx = new TestFixture();
        var middleware = new PersistenceMiddleware(
            fx.Repository, fx.UnitOfWork, fs, fx.Clock,
            Substitute.For<ILogger<PersistenceMiddleware>>());
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/heic/photo.heic", "heic/photo.heic", "/abs/mov/photo.mov", "mov/photo.mov"));

        await middleware.InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.MediaType.Should().Be(MediaType.Image);
    }
}
