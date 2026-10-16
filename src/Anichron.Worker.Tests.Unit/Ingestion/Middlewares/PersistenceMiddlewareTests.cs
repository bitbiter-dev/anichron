using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using Npgsql;
using NSubstitute.ExceptionExtensions;
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
                    LastWriteTime = new DateTimeOffset(new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Local)),
                },
            });
        }

        public IClock Clock { get; }

        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();

        public IVideoProxyGenerator MovGenerator { get; } = Substitute.For<IVideoProxyGenerator>();

        public PersistenceMiddleware Build()
        {
            MovGenerator.FileName.Returns("video_720p.mp4");
            MovGenerator.ProxyType.Returns(ProxyType.WebVideo);
            MovGenerator.TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    FileSystem.File.WriteAllBytes(call.ArgAt<string>(1), [0x01]);
                    return Task.CompletedTask;
                });
            var stagingWriter = new ProxyStagingWriter(
                FileSystem,
                Options.Create(new WorkerSettings { ProxyPath = "/proxies" }),
                Clock,
                GuidFactory,
                Substitute.For<ILogger<ProxyStagingWriter>>());
            return new(Repository, UnitOfWork, FileSystem, Clock,
                new PairedMovResolver(Repository, FileSystem, Clock, GuidFactory),
                new PairedMovProxyWriter(
                    [MovGenerator], new TwoLevelHexShardStrategy(), stagingWriter, Repository,
                    Substitute.For<ILogger<PairedMovProxyWriter>>()),
                Substitute.For<ILogger<PersistenceMiddleware>>());
        }
    }

    private static readonly ExifData DefaultExif =
        new(100, 200, 0, new LocalDateTime(2023, 6, 15, 10, 0), null, null, null, null, null, null);

    private static IngestionContext MakeContext(
        IngestionItem? item = null,
        string? contentHash = "deadbeef",
        ExifData? exif = null)
    {
        var resolvedItem = item ?? new SingleFileItem("/abs/photo.jpg", "photo.jpg", MediaType.Image);
        return new IngestionContext
        {
            Item = resolvedItem,
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AssetId = Guid.NewGuid(),
            ContentHash = contentHash,
            SecondaryHash = resolvedItem.SecondaryFile is not null ? "cafebabe" : null,
            Exif = exif ?? DefaultExif,
        };
    }

    private static DbUpdateException UniqueViolation(string constraintName)
        => new("duplicate key", new PostgresException(
            "duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            null, null, 0, 0, null, null, null, null, null, null,
            constraintName, null, null, null));

    private static Task NoOpNextAsync(IngestionContext ctx, CancellationToken ct)
    {
        _ = (ctx, ct);
        return Task.CompletedTask;
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
        context.Asset.Day.Should().Be(15);
        context.Asset.Year.Should().Be(2023);
    }

    [Fact]
    public async Task InvokeAsync_ExifDateDiffersFromTheFileDate_TheExifDateWinsAsync()
    {
        var fx = new TestFixture();
        var exifDate = new LocalDateTime(2020, 1, 2, 3, 4, 5);
        var context = MakeContext(exif: DefaultExif with { DateCaptured = exifDate });

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.DateCaptured.Should().Be(exifDate);
        (context.Asset.Year, context.Asset.Month, context.Asset.Day).Should().Be((2020, 1, 2));
    }

    [Fact]
    public async Task InvokeAsync_NewAsset_IsNotSoftDeletedAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.IsSoftDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WithNullDateCaptured_FallsBackToFileLastWriteTimeAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(exif: DefaultExif with { DateCaptured = null });

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.Year.Should().Be(2023);
        context.Asset.Month.Should().Be(6);
        context.Asset.Day.Should().Be(15);
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
        context.Asset.Metadata.Height.Should().Be(200);
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
    public async Task InvokeAsync_LivePhotoPairItem_HeicHasLivePhotoMediaTypeAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.MediaType.Should().Be(MediaType.LivePhoto);
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairItem_AddsTwoAssetsToRepositoryAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        fx.Repository.Received(2).Add(Arg.Any<MediaAsset>());
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairItem_MovHasVideoMediaTypeAsync()
    {
        MediaAsset? movAsset = null;
        var fx = new TestFixture();
        fx.Repository.When(r => r.Add(Arg.Is<MediaAsset>(a => a.MediaType == MediaType.Video)))
            .Do(call => movAsset = call.Arg<MediaAsset>());
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        movAsset.Should().NotBeNull();
        movAsset.ContentHash.Should().Be("cafebabe");
        movAsset.IsSoftDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairItem_HeicPairedAssetIdPointsToMovAsync()
    {
        MediaAsset? movAsset = null;
        var fx = new TestFixture();
        fx.Repository.When(r => r.Add(Arg.Is<MediaAsset>(a => a.MediaType == MediaType.Video)))
            .Do(call => movAsset = call.Arg<MediaAsset>());
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.PairedAssetId.Should().Be(movAsset!.Id);
    }

    [Fact]
    public async Task InvokeAsync_Always_UsesContextAssetIdForPrimaryAssetAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.Id.Should().Be(context.AssetId);
    }

    [Fact]
    public async Task InvokeAsync_Always_CopiesProxyFilesFromContextToAssetAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();
        var proxyFile = new ProxyFile { Id = Guid.NewGuid(), AssetId = context.AssetId, ProxyType = ProxyType.Thumbnail };
        context.AddProxyFile(proxyFile);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.ProxyFiles.Should().ContainSingle(p => p.Id == proxyFile.Id);
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairItem_UsesGuidFactoryForSecondaryAssetIdAsync()
    {
        var fx = new TestFixture();
        var expectedId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        fx.GuidFactory.NewGuid().Returns(expectedId);
        MediaAsset? movAsset = null;
        fx.Repository.When(r => r.Add(Arg.Is<MediaAsset>(a => a.MediaType == MediaType.Video)))
            .Do(call => movAsset = call.Arg<MediaAsset>());
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        movAsset!.Id.Should().Be(expectedId);
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairWithMovAlreadyPersisted_PairsHeicToExistingMovAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));
        var existingMov = new MediaAsset
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ContentHash = "cafebabe",
            ProxyFiles = [new ProxyFile { ProxyType = ProxyType.WebVideo }],
        };
        fx.Repository.FindByHashWithProxyFilesAsync("cafebabe", context.Config.Id, Arg.Any<CancellationToken>())
            .Returns(existingMov);
        var nextCalled = false;

        await fx.Build().InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        fx.Repository.Received(1).Add(Arg.Any<MediaAsset>());
        fx.Repository.Received(1).Add(Arg.Is<MediaAsset>(a => a.Id == context.AssetId));
        context.Asset!.PairedAssetId.Should().Be(existingMov.Id);
        nextCalled.Should().BeTrue();
        await fx.MovGenerator.DidNotReceive().TranscodeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fx.Repository.DidNotReceive().AddProxyFile(Arg.Any<ProxyFile>());
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairItem_TranscodesTheMovAsTheMovsOwnProxyAsync()
    {
        var fx = new TestFixture();
        var movId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        fx.GuidFactory.NewGuid().Returns(movId, Guid.Parse("99999999-9999-9999-9999-999999999999"));
        var added = new List<ProxyFile>();
        fx.Repository.When(r => r.AddProxyFile(Arg.Any<ProxyFile>())).Do(call => added.Add(call.Arg<ProxyFile>()));
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await fx.MovGenerator.Received(1).TranscodeAsync("/abs/photo.mov", Arg.Any<string>(), Arg.Any<CancellationToken>());
        var row = added.Should().ContainSingle().Which;
        row.AssetId.Should().Be(movId);
        row.ProxyType.Should().Be(ProxyType.WebVideo);
        context.Asset!.ProxyFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_Always_CopiesOnlyTheAssetsOwnProxyFilesToItAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();
        var own = new ProxyFile { Id = Guid.NewGuid(), AssetId = context.AssetId, ProxyType = ProxyType.Thumbnail };
        context.AddProxyFile(own);
        context.AddProxyFile(new ProxyFile { Id = Guid.NewGuid(), AssetId = Guid.NewGuid(), ProxyType = ProxyType.WebVideo });

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Asset!.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(own);
    }

    // ==========================================================================
    // InvokeAsync — unique violations
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_ActiveHashConflictWithPersistedPrimary_SkipsWithoutThrowingAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Repository.FindByHashAsync("deadbeef", context.Config.Id, Arg.Any<CancellationToken>())
            .Returns(new MediaAsset { ContentHash = "deadbeef" });
        var nextCalled = false;

        var act = () => fx.Build().InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        await act.Should().NotThrowAsync();
        nextCalled.Should().BeFalse();
        context.Asset.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_ActiveHashConflictWithoutPersistedPrimary_RethrowsAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Repository.FindByHashAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((MediaAsset?)null);

        var act = () => fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task InvokeAsync_OtherUniqueViolation_RethrowsWithoutLookupAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(UniqueViolation("IX_MediaAssets_StorageConfigId_FilePath"));

        var act = () => fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        await fx.Repository.DidNotReceive()
            .FindByHashAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
