using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class KnownFileReconcilerTests
{
    private static readonly Instant FirstSeen = Instant.FromUtc(2025, 1, 1, 0, 0);
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 23, 9, 0);
    private static readonly Guid PathRowId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid MovedRowId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid BurstId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid NewProxyId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid PhotoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IBurstRepository Bursts { get; } = Substitute.For<IBurstRepository>();
        public FakeLogger<KnownFileReconciler> Logger { get; } = new();
        public List<ProxyFile> Added { get; } = [];
        public List<ProxyFile> Removed { get; } = [];
        public MockFileSystem FileSystem { get; } = new();
        public IVideoProxyGenerator MovGenerator { get; } = Substitute.For<IVideoProxyGenerator>();
        public IngestionContext Context { get; set; } = new()
        {
            Item = new SingleFileItem("/abs/2024/taken.jpg", "2024/taken.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AttemptId = Guid.NewGuid(),
        };

        public TestFixture()
        {
            Repository.When(r => r.AddProxyFiles(Arg.Any<IEnumerable<ProxyFile>>())).Do(call => Added.AddRange(call.Arg<IEnumerable<ProxyFile>>()));
            Repository.When(r => r.RemoveProxyFile(Arg.Any<ProxyFile>())).Do(call => Removed.Add(call.Arg<ProxyFile>()));
            MovGenerator.FileName.Returns("video_720p.mp4");
            MovGenerator.ProxyType.Returns(ProxyType.WebVideo);
            MovGenerator.TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => FileSystem.File.WriteAllBytesAsync(call.ArgAt<string>(1), [0x01, 0x02]));
        }

        public void LoadsMetadata(MediaAsset asset, Metadata? metadata)
            => Repository.LoadMetadataAsync(asset, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                asset.Metadata = metadata;
                return Task.CompletedTask;
            });

        public Task<bool> ReconcileAsync(FileClassification file, string relativePath = "2024/taken.jpg")
        {
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            var guids = Substitute.For<IGuidFactory>();
            guids.NewGuid().Returns(NewProxyId);
            var movProxyWriter = new PairedMovProxyWriter(
                [MovGenerator],
                new TwoLevelHexShardStrategy(),
                new ProxyStagingWriter(FileSystem, clock, guids, new FakeLogger<ProxyStagingWriter>()),
                Repository,
                new FakeLogger<PairedMovProxyWriter>());
            return new KnownFileReconciler(
                    clock, Repository, new StoredContent(Repository, Bursts, clock), guids, movProxyWriter, FileSystem, Logger)
                .ReconcileAsync(Context, file, relativePath, CancellationToken.None);
        }
    }

    private static IngestionContext VideoContext(FileOutcome outcome, MediaAsset video, bool withPhoto = true)
    {
        var photo = withPhoto ? Row(PhotoId, "2024/live.heic", "abc123") : null;
        photo?.DateCaptured = new LocalDateTime(2020, 7, 8, 9, 10);
        return new IngestionContext
        {
            Item = new LivePhotoPairItem("/abs/2024/live.heic", "2024/live.heic", "/abs/2024/live.mov", "2024/live.mov"),
            Config = new UserStorageConfig { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AttemptId = Guid.NewGuid(),
            ContentHash = "abc123",
            SecondaryHash = "cafebabe",
            Classification = new(
                new FileClassification(photo is null ? FileOutcome.New : FileOutcome.Unchanged, photo),
                new FileClassification(outcome, video),
                PairingAction.None),
        };
    }

    private static MediaAsset Row(Guid id, string path, string hash, bool softDeleted = false) => new()
    {
        Id = id,
        FilePath = path,
        FileName = Path.GetFileName(path),
        ContentHash = hash,
        MediaType = MediaType.Image,
        IsSoftDeleted = softDeleted,
        LastSeenOnNas = FirstSeen,
        DateCaptured = new LocalDateTime(2019, 3, 4, 5, 6),
    };

    private static ProxyFile Proxy(Guid owner, ProxyType type, string path, long size = 1) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = owner,
        ProxyType = type,
        ProxyPath = path,
        SizeBytes = size,
        CreatedAt = FirstSeen,
    };

    [Fact]
    public async Task ReconcileAsync_MovedFile_KeepsItsBurstAsync()
    {
        var fx = new TestFixture();
        var stored = Row(MovedRowId, "2024/a.heic", "abc123");
        stored.BurstId = BurstId;

        var reconciled = await fx.ReconcileAsync(new FileClassification(FileOutcome.Moved, stored), "2025/a.heic");

        reconciled.Should().BeTrue();
        stored.Should().BeEquivalentTo(new { FilePath = "2025/a.heic", BurstId = (Guid?)BurstId, LastSeenOnNas = Now });
    }

    // ==========================================================================
    // Overwritten by a copy (row 2)
    // ==========================================================================

    [Fact]
    public async Task ReconcileAsync_OverwrittenByCopy_SoftDeletesThePathsRowAndLeavesTheCopiedOneAsync()
    {
        var fx = new TestFixture();
        var copied = Row(MovedRowId, "2023/photo.jpg", "abc123");
        var atPath = Row(PathRowId, "2024/taken.jpg", "f00d");

        var reconciled = await fx.ReconcileAsync(new FileClassification(FileOutcome.OverwrittenByCopy, copied, Retired: atPath));

        reconciled.Should().BeTrue();
        atPath.Should().BeEquivalentTo(new { IsSoftDeleted = true, ContentHash = "f00d", FilePath = "2024/taken.jpg" });
        copied.Should().BeEquivalentTo(new { IsSoftDeleted = false, FilePath = "2023/photo.jpg", LastSeenOnNas = FirstSeen });
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((1, 0));
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information).Which.Message.Should().Be(
            "Soft-deleted asset eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee at 2024/taken.jpg: the path now holds a copy of 2023/photo.jpg.");
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Debug).Which.Message.Should().Be(
            "Duplicate content abc123 in storage config 00000000-0000-0000-0000-000000000000: 2024/taken.jpg is a copy of 2023/photo.jpg.");
    }

    // ==========================================================================
    // Overwritten by a move (rows 4 and 5)
    // ==========================================================================

    private static (MediaAsset AtPath, MediaAsset Moved) MoveOntoAnotherRow(TestFixture fx, bool atPathSoftDeleted = false)
    {
        var atPath = Row(PathRowId, "2024/taken.jpg", "f00d", softDeleted: atPathSoftDeleted);
        atPath.BurstId = BurstId;
        atPath.PairedAssetId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        atPath.Interactions = [new AssetInteraction { AssetId = PathRowId, IsStarred = true }];
        atPath.ProxyFiles =
        [
            Proxy(PathRowId, ProxyType.Thumbnail, "f0/old/thumbnail.jpg"),
            Proxy(PathRowId, ProxyType.WebVideo, "f0/old/video_720p.mp4"),
        ];
        var moved = Row(MovedRowId, "2023/photo.jpg", "abc123");
        moved.MediaType = MediaType.LivePhoto;
        moved.DateCaptured = new LocalDateTime(2022, 8, 9, 7, 30);
        moved.ProxyFiles =
        [
            Proxy(MovedRowId, ProxyType.Thumbnail, "ab/new/thumbnail.jpg", size: 300),
            Proxy(MovedRowId, ProxyType.FullPreview, "ab/new/preview.jpg", size: 1920),
        ];
        fx.LoadsMetadata(atPath, new Metadata { AssetId = PathRowId, Width = 10, Height = 10, CameraMake = "Old", Blurhash = "old" });
        fx.LoadsMetadata(moved, new Metadata
        {
            AssetId = MovedRowId,
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
        return (atPath, moved);
    }

    [Fact]
    public async Task ReconcileAsync_OverwrittenByMove_ThePathsRowTakesTheMovedRowsContentAsync()
    {
        var fx = new TestFixture();
        var (atPath, moved) = MoveOntoAnotherRow(fx);

        var reconciled = await fx.ReconcileAsync(new FileClassification(FileOutcome.OverwrittenByMove, atPath, Retired: moved));

        reconciled.Should().BeTrue();
        atPath.Should().BeEquivalentTo(new
        {
            Id = PathRowId,
            FilePath = "2024/taken.jpg",
            ContentHash = "abc123",
            MediaType = MediaType.LivePhoto,
            DateCaptured = new LocalDateTime(2022, 8, 9, 7, 30),
            LastSeenOnNas = Now,
            IsSoftDeleted = false,
            BurstId = (Guid?)null,
            PairedAssetId = (Guid?)Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        });
        atPath.Interactions.Should().ContainSingle().Which.IsStarred.Should().BeTrue();
        atPath.Metadata.Should().BeEquivalentTo(new Metadata
        {
            AssetId = PathRowId,
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
        }, options => options.Excluding(m => m.Asset));
        await fx.Bursts.Received(1).RemoveCheckAsync(PathRowId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_OverwrittenByMove_CarriesEveryMetadataValueOfTheMovedRowAsync()
    {
        var fx = new TestFixture();
        var (atPath, moved) = MoveOntoAnotherRow(fx);
        var movedMetadata = MetadataValuesTests.FullyPopulated();
        movedMetadata.AssetId = MovedRowId;
        fx.LoadsMetadata(moved, movedMetadata);

        await fx.ReconcileAsync(new FileClassification(FileOutcome.OverwrittenByMove, atPath, Retired: moved));

        atPath.Metadata.Should().BeEquivalentTo(movedMetadata, options => options.Excluding(m => m.AssetId).Excluding(m => m.Asset));
        atPath.Metadata.AssetId.Should().Be(PathRowId);
    }

    [Fact]
    public async Task ReconcileAsync_OverwrittenByMove_UpdatesThePathsStoredMetadataRowInPlaceAsync()
    {
        var fx = new TestFixture();
        var (atPath, moved) = MoveOntoAnotherRow(fx);
        var stored = new Metadata { AssetId = PathRowId, Width = 10 };
        fx.LoadsMetadata(atPath, stored);

        await fx.ReconcileAsync(new FileClassification(FileOutcome.OverwrittenByMove, atPath, Retired: moved));

        atPath.Metadata.Should().BeSameAs(stored);
        stored.Width.Should().Be(4032);
    }

    [Fact]
    public async Task ReconcileAsync_OverwrittenByMove_ThePathsRowReusesTheMovedRowsProxyFilesAsync()
    {
        var fx = new TestFixture();
        var (atPath, moved) = MoveOntoAnotherRow(fx);
        var thumbnail = atPath.ProxyFiles.Single(p => p.ProxyType == ProxyType.Thumbnail);
        var webVideo = atPath.ProxyFiles.Single(p => p.ProxyType == ProxyType.WebVideo);

        await fx.ReconcileAsync(new FileClassification(FileOutcome.OverwrittenByMove, atPath, Retired: moved));

        thumbnail.Should().BeEquivalentTo(new { AssetId = PathRowId, ProxyPath = "ab/new/thumbnail.jpg", SizeBytes = 300L, CreatedAt = FirstSeen });
        fx.Removed.Should().ContainSingle().Which.Should().BeSameAs(webVideo);
        fx.Added.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = NewProxyId,
            AssetId = PathRowId,
            ProxyType = ProxyType.FullPreview,
            ProxyPath = "ab/new/preview.jpg",
            SizeBytes = 1920L,
            CreatedAt = FirstSeen,
        });
        moved.ProxyFiles.Select(p => (p.AssetId, p.ProxyPath)).Should().Equal(
            (MovedRowId, "ab/new/thumbnail.jpg"), (MovedRowId, "ab/new/preview.jpg"));
    }

    [Fact]
    public async Task ReconcileAsync_OverwrittenByMove_SoftDeletesTheMovedRowAsync()
    {
        var fx = new TestFixture();
        var (atPath, moved) = MoveOntoAnotherRow(fx);

        await fx.ReconcileAsync(new FileClassification(FileOutcome.OverwrittenByMove, atPath, Retired: moved));

        moved.Should().BeEquivalentTo(new { IsSoftDeleted = true, FilePath = "2023/photo.jpg", ContentHash = "abc123", LastSeenOnNas = FirstSeen });
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((1, 0));
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information).Which.Message.Should().Be(
            "Asset eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee at 2024/taken.jpg took the content of asset "
            + "dddddddd-dddd-dddd-dddd-dddddddddddd, moved there from 2023/photo.jpg; soft-deleted asset dddddddd-dddd-dddd-dddd-dddddddddddd.");
    }

    [Fact]
    public async Task ReconcileAsync_OverwrittenByMoveOntoASoftDeletedRow_AutoRestoresItAsync()
    {
        var fx = new TestFixture();
        var (atPath, moved) = MoveOntoAnotherRow(fx, atPathSoftDeleted: true);

        await fx.ReconcileAsync(new FileClassification(FileOutcome.OverwrittenByMove, atPath, Retired: moved));

        atPath.Should().BeEquivalentTo(new { IsSoftDeleted = false, ContentHash = "abc123" });
        moved.IsSoftDeleted.Should().BeTrue();
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((1, 1));
    }

    [Fact]
    public async Task ReconcileAsync_OverwrittenByMoveFromARowWithoutMetadata_ClearsThePathsMetadataAsync()
    {
        var fx = new TestFixture();
        var (atPath, moved) = MoveOntoAnotherRow(fx);
        fx.LoadsMetadata(moved, null);

        await fx.ReconcileAsync(new FileClassification(FileOutcome.OverwrittenByMove, atPath, Retired: moved));

        atPath.Metadata.Should().BeEquivalentTo(new Metadata { AssetId = PathRowId }, options => options.Excluding(m => m.Asset));
    }

    [Fact]
    public async Task ReconcileAsync_OverwrittenByMoveOntoARowWithoutMetadata_GivesItAMetadataRowAsync()
    {
        var fx = new TestFixture();
        var (atPath, moved) = MoveOntoAnotherRow(fx);
        fx.LoadsMetadata(atPath, null);

        await fx.ReconcileAsync(new FileClassification(FileOutcome.OverwrittenByMove, atPath, Retired: moved));

        atPath.Metadata.Should().NotBeNull();
        atPath.Metadata.Should().BeEquivalentTo(new { AssetId = PathRowId, Width = 4032, CameraMake = "Fujifilm" });
    }

    // ==========================================================================
    // Auto-restore (rows 8 and 10, and a Live Photo MOV under row 9)
    // ==========================================================================

    [Fact]
    public async Task ReconcileAsync_AutoRestored_ReactivatesTheRowAndClearsItsBurstCheckAsync()
    {
        var fx = new TestFixture();
        var trashed = Row(PathRowId, "2024/taken.jpg", "abc123", softDeleted: true);
        trashed.Interactions = [new AssetInteraction { AssetId = PathRowId, IsHidden = true }];
        trashed.BurstId = BurstId;

        var reconciled = await fx.ReconcileAsync(new FileClassification(FileOutcome.AutoRestored, trashed));

        reconciled.Should().BeTrue();
        trashed.Should().BeEquivalentTo(new
        {
            IsSoftDeleted = false,
            LastSeenOnNas = Now,
            FilePath = "2024/taken.jpg",
            ContentHash = "abc123",
            BurstId = (Guid?)BurstId,
        });
        trashed.Interactions.Should().ContainSingle().Which.IsHidden.Should().BeTrue();
        await fx.Bursts.Received(1).RemoveCheckAsync(PathRowId, Arg.Any<CancellationToken>());
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((0, 1));
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle().Which.Should().Match<FakeLogRecord>(r =>
            r.Level == LogLevel.Debug && r.Message == "Auto-restored asset eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee at 2024/taken.jpg.");
    }

    [Fact]
    public async Task ReconcileAsync_AutoRestoredMoved_ReactivatesTheRowAtTheNewPathAsync()
    {
        var fx = new TestFixture();
        var trashed = Row(MovedRowId, "2020/photo.jpg", "abc123", softDeleted: true);

        await fx.ReconcileAsync(new FileClassification(FileOutcome.AutoRestoredMoved, trashed));

        trashed.Should().BeEquivalentTo(new { IsSoftDeleted = false, LastSeenOnNas = Now, FilePath = "2024/taken.jpg", FileName = "taken.jpg" });
        await fx.Bursts.Received(1).RemoveCheckAsync(MovedRowId, Arg.Any<CancellationToken>());
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((0, 1));
    }

    [Fact]
    public async Task ReconcileAsync_VideoWithOtherContentAtItsSoftDeletedRow_IsAutoRestoredWithTheNewContentAsync()
    {
        var fx = new TestFixture();
        var mov = Row(MovedRowId, "2024/live.mov", "0ldm0v", softDeleted: true);
        fx.Context = VideoContext(FileOutcome.AutoRestoredContentChanged, mov);

        var reconciled = await fx.ReconcileAsync(fx.Context.Classification!.Secondary!, "2024/live.mov");

        reconciled.Should().BeTrue();
        mov.Should().BeEquivalentTo(new { IsSoftDeleted = false, LastSeenOnNas = Now, ContentHash = "cafebabe" });
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((0, 1));
    }

    [Fact]
    public async Task ReconcileAsync_VideoContentChanged_TakesTheNewContentWithTheCaptureDateOfItsPhotoAsync()
    {
        var fx = new TestFixture();
        var mov = Row(MovedRowId, "2023/live.mov", "0ldm0v");
        mov.MediaType = MediaType.LivePhoto;
        mov.BurstId = BurstId;
        var oldProxy = Proxy(MovedRowId, ProxyType.WebVideo, "0l/old/video_720p.mp4");
        mov.ProxyFiles = [oldProxy];
        fx.LoadsMetadata(mov, new Metadata { AssetId = MovedRowId, Width = 1920, Height = 1080 });
        fx.Context = VideoContext(FileOutcome.ContentChanged, mov);

        var reconciled = await fx.ReconcileAsync(fx.Context.Classification!.Secondary!, "2024/live.mov");

        reconciled.Should().BeTrue();
        mov.Should().BeEquivalentTo(new
        {
            FilePath = "2024/live.mov",
            FileName = "live.mov",
            ContentHash = "cafebabe",
            DateCaptured = new LocalDateTime(2020, 7, 8, 9, 10),
            MediaType = MediaType.Video,
            BurstId = (Guid?)null,
            LastSeenOnNas = Now,
        });
        mov.Metadata.Should().BeEquivalentTo(new { Width = 0, Height = 0 });
        oldProxy.ProxyPath.Should().Be($"ca/{fx.Context.Config.Id:N}-cafebabe/video_720p.mp4");
        oldProxy.SizeBytes.Should().Be(2);
        fx.Added.Should().BeEmpty();
        await fx.MovGenerator.Received(1).TranscodeAsync("/abs/2024/live.mov", Arg.Any<string>(), Arg.Any<CancellationToken>());
        fx.FileSystem.File.Exists($"/data/proxies/ca/{fx.Context.Config.Id:N}-cafebabe/video_720p.mp4").Should().BeTrue();
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((0, 0));
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information)
            .Which.Message.Should().Be(
                "Video asset dddddddd-dddd-dddd-dddd-dddddddddddd at 2024/live.mov changed content from 0ldm0v to cafebabe, updating it in place.");
    }

    [Fact]
    public async Task ReconcileAsync_VideoContentChangedWithoutAPhotoRow_TakesTheVideoFilesLastWriteTimeAsync()
    {
        var fx = new TestFixture();
        var mov = Row(MovedRowId, "2024/live.mov", "0ldm0v");
        fx.Context = VideoContext(FileOutcome.ContentChanged, mov, withPhoto: false);
        fx.FileSystem.AddFile("/abs/2024/live.mov", new MockFileData([]) { LastWriteTime = new DateTime(2021, 2, 3, 4, 5, 6, DateTimeKind.Local) });

        await fx.ReconcileAsync(fx.Context.Classification!.Secondary!, "2024/live.mov");

        mov.DateCaptured.Should().Be(new LocalDateTime(2021, 2, 3, 4, 5, 6));
    }

    [Fact]
    public async Task ReconcileAsync_DuplicateCopy_ChangesNothingAsync()
    {
        var fx = new TestFixture();
        var stored = Row(MovedRowId, "2023/photo.jpg", "abc123");

        (await fx.ReconcileAsync(new FileClassification(FileOutcome.DuplicateCopy, stored))).Should().BeFalse();
        stored.LastSeenOnNas.Should().Be(FirstSeen);
    }

    [Fact]
    public async Task ReconcileAsync_LoneLivePhotoVideoOverAStandaloneRow_SoftDeletesOnlyTheStandaloneRowAsync()
    {
        var fx = new TestFixture();
        var video = Row(MovedRowId, "LP/A.mov", "cafebabe");
        var standalone = Row(PathRowId, "X.mov", "f00d");

        var reconciled = await fx.ReconcileAsync(
            new FileClassification(FileOutcome.LoneLivePhotoVideo, video, Retired: standalone), "X.mov");

        reconciled.Should().BeTrue();
        standalone.Should().BeEquivalentTo(new { IsSoftDeleted = true, ContentHash = "f00d", FilePath = "X.mov" });
        video.Should().BeEquivalentTo(new { IsSoftDeleted = false, FilePath = "LP/A.mov", LastSeenOnNas = FirstSeen });
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((1, 0));
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle().Which.Should().Match<FakeLogRecord>(r =>
            r.Level == LogLevel.Information
            && r.Message == "Soft-deleted asset eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee at X.mov: the path now holds a copy of "
                + "the video of a Live Photo, dddddddd-dddd-dddd-dddd-dddddddddddd at LP/A.mov.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconcileAsync_LoneLivePhotoVideoRetiringNothing_LeavesTheVideoRowAloneAsync(bool softDeleted)
    {
        var fx = new TestFixture();
        var video = Row(MovedRowId, "LP/A.mov", "cafebabe", softDeleted);

        var reconciled = await fx.ReconcileAsync(new FileClassification(FileOutcome.LoneLivePhotoVideo, video), "elsewhere/A.mov");

        reconciled.Should().BeFalse();
        video.Should().BeEquivalentTo(new { IsSoftDeleted = softDeleted, FilePath = "LP/A.mov", LastSeenOnNas = FirstSeen });
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((0, 0));
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle().Which.Should().Match<FakeLogRecord>(r =>
            r.Level == LogLevel.Debug
            && r.Message == "Left elsewhere/A.mov alone: it holds the video of a Live Photo, asset "
                + "dddddddd-dddd-dddd-dddd-dddddddddddd, which follows only its photo.");
    }

    private static IngestionClassification LivePhoto(FileOutcome photoOutcome, MediaAsset video, Guid? photoPointsAt = null)
    {
        var photo = Row(PhotoId, "2024/live.heic", "abc123", softDeleted: photoOutcome == FileOutcome.AutoRestored);
        photo.PairedAssetId = photoPointsAt ?? video.Id;
        return new(
            new FileClassification(photoOutcome, photo),
            new FileClassification(FileOutcome.AutoRestored, video),
            PairingAction.None);
    }

    private static IEnumerable<FakeLogRecord> InformationLines(TestFixture fx)
        => fx.Logger.Collector.GetSnapshot().Where(r => r.Level == LogLevel.Information);

    [Theory]
    [InlineData((int)FileOutcome.Unchanged)]
    [InlineData((int)FileOutcome.Moved)]
    [InlineData((int)FileOutcome.ContentChanged)]
    public async Task ReconcileAsync_VideoAutoRestoredBesideItsActivePhoto_SaysItIsALivePhotoAgainAsync(int photoOutcome)
    {
        var fx = new TestFixture();
        var video = Row(MovedRowId, "2024/live.mov", "cafebabe", softDeleted: true);
        fx.Context.Classification = LivePhoto((FileOutcome)photoOutcome, video);

        await fx.ReconcileAsync(fx.Context.Classification.Secondary!, "2024/live.mov");

        InformationLines(fx).Should().ContainSingle().Which.Message.Should().Be(
            "Asset cccccccc-cccc-cccc-cccc-cccccccccccc is a Live Photo again: "
            + "its video, asset dddddddd-dddd-dddd-dddd-dddddddddddd at 2024/live.mov, was auto-restored.");
    }

    [Theory]
    [InlineData((int)FileOutcome.AutoRestored)]
    [InlineData((int)FileOutcome.AutoRestoredMoved)]
    [InlineData((int)FileOutcome.AutoRestoredContentChanged)]
    public async Task ReconcileAsync_VideoAutoRestoredWithItsPhoto_SaysNothingMoreAsync(int photoOutcome)
    {
        var fx = new TestFixture();
        var video = Row(MovedRowId, "2024/live.mov", "cafebabe", softDeleted: true);
        fx.Context.Classification = LivePhoto((FileOutcome)photoOutcome, video);

        await fx.ReconcileAsync(fx.Context.Classification.Secondary!, "2024/live.mov");

        InformationLines(fx).Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_VideoAutoRestoredBesideAPhotoPointingElsewhere_SaysNothingMoreAsync()
    {
        var fx = new TestFixture();
        var video = Row(MovedRowId, "2024/live.mov", "cafebabe", softDeleted: true);
        fx.Context.Classification = LivePhoto(FileOutcome.Unchanged, video, photoPointsAt: Guid.NewGuid());

        await fx.ReconcileAsync(fx.Context.Classification.Secondary!, "2024/live.mov");

        InformationLines(fx).Should().BeEmpty();
    }
}
