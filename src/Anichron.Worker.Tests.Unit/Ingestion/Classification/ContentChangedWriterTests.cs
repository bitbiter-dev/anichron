using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using NSubstitute.ExceptionExtensions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class ContentChangedWriterTests
{
    private static readonly Guid StoredId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid MovId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BurstId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Instant FirstSeen = Instant.FromUtc(2024, 1, 1, 0, 0, 0);
    private static readonly Instant Now = Instant.FromUtc(2026, 5, 19, 10, 0, 0);

    private static readonly ExifData EditedExif = new(
        Width: 4032,
        Height: 3024,
        Orientation: 6,
        DateCaptured: new LocalDateTime(2022, 8, 9, 7, 30),
        Latitude: 35.68,
        Longitude: 139.69,
        CameraMake: "Fujifilm",
        CameraModel: "X100V",
        LensModel: "23mm F2",
        DurationInSeconds: 3);

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();
        public IVideoProxyGenerator MovGenerator { get; } = Substitute.For<IVideoProxyGenerator>();
        public MockFileSystem FileSystem { get; } = new();
        public List<ProxyFile> Added { get; } = [];
        public List<ProxyFile> Removed { get; } = [];
        public Metadata StoredMetadata { get; } = new() { AssetId = StoredId, Width = 10, Height = 10, CameraMake = "Old" };
        public MediaAsset Stored { get; }

        public TestFixture()
        {
            Stored = new MediaAsset
            {
                Id = StoredId,
                StorageConfigId = Guid.NewGuid(),
                BurstId = BurstId,
                FilePath = "2022/photo.jpg",
                FileName = "photo.jpg",
                ContentHash = "0ldc0ntent",
                MediaType = MediaType.Image,
                LastSeenOnNas = FirstSeen,
                ProxyFiles =
                [
                    OldProxy(ProxyType.Thumbnail, "0l/old/thumbnail.jpg"),
                    OldProxy(ProxyType.FullPreview, "0l/old/preview.jpg"),
                ],
                Interactions = [new AssetInteraction { AssetId = StoredId, UserId = Guid.NewGuid(), IsStarred = true }],
            };
            Stored.SetCaptureDate(new LocalDateTime(2019, 3, 4, 5, 6));
            Repository.LoadMetadataAsync(Stored, Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    Stored.Metadata = StoredMetadata;
                    return Task.CompletedTask;
                });
            Repository.When(r => r.AddProxyFile(Arg.Any<ProxyFile>())).Do(call => Added.Add(call.Arg<ProxyFile>()));
            Repository.When(r => r.RemoveProxyFile(Arg.Any<ProxyFile>())).Do(call => Removed.Add(call.Arg<ProxyFile>()));
        }

        public ContentChangedWriter Build()
        {
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            MovGenerator.FileName.Returns("video_720p.mp4");
            MovGenerator.ProxyType.Returns(ProxyType.WebVideo);
            MovGenerator.TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => FileSystem.File.WriteAllBytesAsync(call.ArgAt<string>(1), [0x01]));
            var stagingWriter = new ProxyStagingWriter(FileSystem, clock, GuidFactory, new FakeLogger<ProxyStagingWriter>());
            return new(Repository, new IngestionSaver(UnitOfWork, Repository), FileSystem, clock,
                new PairedMovResolver(Repository, FileSystem, clock, GuidFactory),
                new PairedMovProxyWriter(
                    [MovGenerator], new TwoLevelHexShardStrategy(), stagingWriter, Repository,
                    new FakeLogger<PairedMovProxyWriter>()),
                new FakeLogger<ContentChangedWriter>());
        }

        public IngestionContext Context(IngestionItem? item = null, MediaAsset? storedMov = null)
        {
            var resolvedItem = item ?? new SingleFileItem("/abs/2022/photo.jpg", "2022/photo.jpg", MediaType.Image);
            var isPair = resolvedItem.SecondaryFile is not null;
            return new IngestionContext
            {
                Item = resolvedItem,
                Config = new UserStorageConfig { Id = Stored.StorageConfigId, UserId = Guid.NewGuid(), RootPath = "/abs" },
                AttemptId = Guid.NewGuid(),
                AssetId = StoredId,
                ContentHash = "n3wc0ntent",
                SecondaryHash = isPair ? "cafebabe" : null,
                Classification = new IngestionClassification(
                    new FileClassification(FileOutcome.ContentChanged, Stored),
                    isPair ? new FileClassification(FileOutcome.Unchanged, storedMov) : null,
                    PairingAction.None),
                Exif = EditedExif,
                Blurhash = "LEHV6nWB2yk8pyo0adR*.7kCMdnj",
            };
        }

        public Task<MediaAsset?> WriteAsync(IngestionContext context)
            => Build().TryWriteAsync(context, CancellationToken.None);

        private static ProxyFile OldProxy(ProxyType type, string path) => new()
        {
            Id = Guid.NewGuid(),
            AssetId = StoredId,
            ProxyType = type,
            ProxyPath = path,
            SizeBytes = 1,
            CreatedAt = FirstSeen,
        };
    }

    private static ProxyFile NewProxy(ProxyType type, string path, Guid? owner = null) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = owner ?? StoredId,
        ProxyType = type,
        ProxyPath = path,
        SizeBytes = 2048,
        CreatedAt = Now,
    };

    [Fact]
    public void Outcome_IsContentChanged()
        => new TestFixture().Build().Outcome.Should().Be(FileOutcome.ContentChanged);

    // ==========================================================================
    // The row
    // ==========================================================================

    [Fact]
    public async Task TryWriteAsync_KeepsTheStoredRowAndItsIdentityAsync()
    {
        var fx = new TestFixture();
        var interactions = fx.Stored.Interactions.ToList();

        var asset = await fx.WriteAsync(fx.Context());

        asset.Should().BeSameAs(fx.Stored);
        asset.Id.Should().Be(StoredId);
        asset.BurstId.Should().Be(BurstId);
        asset.FilePath.Should().Be("2022/photo.jpg");
        asset.Interactions.Should().Equal(interactions);
        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
    }

    [Fact]
    public async Task TryWriteAsync_DescribesTheNewContentAsync()
    {
        var fx = new TestFixture();

        var asset = await fx.WriteAsync(fx.Context(new SingleFileItem("/abs/2022/photo.jpg", "2022/photo.jpg", MediaType.Video)));

        asset!.ContentHash.Should().Be("n3wc0ntent");
        asset.DateCaptured.Should().Be(new LocalDateTime(2022, 8, 9, 7, 30));
        (asset.Year, asset.Month, asset.Day).Should().Be((2022, 8, 9));
        asset.MediaType.Should().Be(MediaType.Video);
        asset.LastSeenOnNas.Should().Be(Now);
    }

    [Fact]
    public async Task TryWriteAsync_RewritesEveryMetadataFieldOnTheLoadedRowAsync()
    {
        var fx = new TestFixture();

        var asset = await fx.WriteAsync(fx.Context());

        asset!.Metadata.Should().BeSameAs(fx.StoredMetadata);
        asset.Metadata.Should().BeEquivalentTo(new
        {
            AssetId = StoredId,
            Width = 4032,
            Height = 3024,
            Orientation = 6,
            Latitude = 35.68,
            Longitude = 139.69,
            CameraMake = "Fujifilm",
            CameraModel = "X100V",
            LensModel = "23mm F2",
            DurationInSeconds = 3,
            Blurhash = "LEHV6nWB2yk8pyo0adR*.7kCMdnj",
        });
    }

    [Fact]
    public async Task TryWriteAsync_RowWithoutMetadata_GivesItAMetadataRowAsync()
    {
        var fx = new TestFixture();
        fx.Repository.LoadMetadataAsync(fx.Stored, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var asset = await fx.WriteAsync(fx.Context());

        asset!.Metadata.Should().NotBeNull();
        asset.Metadata.AssetId.Should().Be(StoredId);
        asset.Metadata.Width.Should().Be(4032);
    }

    // ==========================================================================
    // Proxy rows
    // ==========================================================================

    [Fact]
    public async Task TryWriteAsync_RegeneratedType_UpdatesTheExistingRowInPlaceAsync()
    {
        var fx = new TestFixture();
        var context = fx.Context();
        var thumbnailRow = fx.Stored.ProxyFiles.Single(p => p.ProxyType == ProxyType.Thumbnail);
        var previewRow = fx.Stored.ProxyFiles.Single(p => p.ProxyType == ProxyType.FullPreview);
        var thumbnailId = thumbnailRow.Id;
        context.AddProxyFile(NewProxy(ProxyType.Thumbnail, "n3/new/thumbnail.jpg"));
        context.AddProxyFile(NewProxy(ProxyType.FullPreview, "n3/new/preview.jpg"));

        await fx.WriteAsync(context);

        thumbnailRow.Id.Should().Be(thumbnailId);
        thumbnailRow.Should().BeEquivalentTo(new { ProxyPath = "n3/new/thumbnail.jpg", SizeBytes = 2048L, CreatedAt = Now });
        previewRow.ProxyPath.Should().Be("n3/new/preview.jpg");
        fx.Added.Should().BeEmpty();
        fx.Removed.Should().BeEmpty();
    }

    [Fact]
    public async Task TryWriteAsync_TypeTheRowLacked_IsAddedAsync()
    {
        var fx = new TestFixture();
        var context = fx.Context();
        var webVideo = NewProxy(ProxyType.WebVideo, "n3/new/video_720p.mp4");
        context.AddProxyFile(NewProxy(ProxyType.Thumbnail, "n3/new/thumbnail.jpg"));
        context.AddProxyFile(NewProxy(ProxyType.FullPreview, "n3/new/preview.jpg"));
        context.AddProxyFile(webVideo);

        await fx.WriteAsync(context);

        fx.Added.Should().ContainSingle().Which.Should().BeSameAs(webVideo);
    }

    [Fact]
    public async Task TryWriteAsync_TypeNoLongerGenerated_IsRemovedAsync()
    {
        var fx = new TestFixture();
        var context = fx.Context();
        var previewRow = fx.Stored.ProxyFiles.Single(p => p.ProxyType == ProxyType.FullPreview);
        context.AddProxyFile(NewProxy(ProxyType.Thumbnail, "n3/new/thumbnail.jpg"));

        await fx.WriteAsync(context);

        fx.Removed.Should().ContainSingle().Which.Should().BeSameAs(previewRow);
    }

    [Fact]
    public async Task TryWriteAsync_ProxiesOfAnotherOwner_AreNotAttachedToTheRowAsync()
    {
        var fx = new TestFixture();
        var context = fx.Context();
        var thumbnailRow = fx.Stored.ProxyFiles.Single(p => p.ProxyType == ProxyType.Thumbnail);
        context.AddProxyFile(NewProxy(ProxyType.Thumbnail, "n3/new/thumbnail.jpg"));
        context.AddProxyFile(NewProxy(ProxyType.FullPreview, "n3/new/preview.jpg"));
        context.AddProxyFile(NewProxy(ProxyType.Thumbnail, "ca/mov/thumbnail.jpg", owner: MovId));

        await fx.WriteAsync(context);

        thumbnailRow.ProxyPath.Should().Be("n3/new/thumbnail.jpg");
        fx.Added.Should().BeEmpty();
    }

    // ==========================================================================
    // Live Photo
    // ==========================================================================

    [Fact]
    public async Task TryWriteAsync_EditedHeic_StaysPairedToItsStoredMovAsync()
    {
        var fx = new TestFixture();
        fx.Stored.PairedAssetId = MovId;
        var mov = new MediaAsset
        {
            Id = MovId,
            ContentHash = "cafebabe",
            ProxyFiles = [new ProxyFile { ProxyType = ProxyType.WebVideo }],
        };
        var context = fx.Context(
            new LivePhotoPairItem("/abs/2022/live.heic", "2022/live.heic", "/abs/2022/live.mov", "2022/live.mov"), mov);

        var asset = await fx.WriteAsync(context);

        asset!.PairedAssetId.Should().Be(MovId);
        asset.MediaType.Should().Be(MediaType.LivePhoto);
        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
        await fx.MovGenerator.DidNotReceive().TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Saving
    // ==========================================================================

    [Fact]
    public async Task TryWriteAsync_SavesOnceAsync()
    {
        var fx = new TestFixture();

        await fx.WriteAsync(fx.Context());

        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryWriteAsync_AnotherConsumerCommittedTheNewContentFirst_SkipsWithoutThrowingAsync()
    {
        var fx = new TestFixture();
        var context = fx.Context();
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Repository.FindByHashAsync("n3wc0ntent", context.Config.Id, Arg.Any<CancellationToken>())
            .Returns(new MediaAsset { ContentHash = "n3wc0ntent" });

        var asset = await fx.WriteAsync(context);

        asset.Should().BeNull();
    }

    [Fact]
    public async Task TryWriteAsync_SaveFailsForAnotherReason_RethrowsAsync()
    {
        var fx = new TestFixture();
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation("IX_MediaAssets_StorageConfigId_FilePath"));

        var act = () => fx.WriteAsync(fx.Context());

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
