using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class KnownFileReconcilerTests
{
    private static readonly Instant FirstSeen = Instant.FromUtc(2025, 1, 1, 0, 0);
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 23, 9, 0);
    private static readonly Guid PathRowId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid MovedRowId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid BurstId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid NewProxyId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IBurstRepository Bursts { get; } = Substitute.For<IBurstRepository>();
        public FakeLogger<KnownFileReconciler> Logger { get; } = new();
        public List<ProxyFile> Added { get; } = [];
        public List<ProxyFile> Removed { get; } = [];
        public IngestionContext Context { get; } = new()
        {
            Item = new SingleFileItem("/abs/2024/taken.jpg", "2024/taken.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AttemptId = Guid.NewGuid(),
        };

        public TestFixture()
        {
            Repository.When(r => r.AddProxyFiles(Arg.Any<IEnumerable<ProxyFile>>())).Do(call => Added.AddRange(call.Arg<IEnumerable<ProxyFile>>()));
            Repository.When(r => r.RemoveProxyFile(Arg.Any<ProxyFile>())).Do(call => Removed.Add(call.Arg<ProxyFile>()));
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
            return new KnownFileReconciler(clock, Repository, Bursts, guids, Logger)
                .ReconcileAsync(Context, file, relativePath, CancellationToken.None);
        }
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
        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((1, 0));
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
        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((1, 0));
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
        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((1, 1));
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
        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((0, 1));
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
        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((0, 1));
    }

    [Fact]
    public async Task ReconcileAsync_MovWithOtherContentAtItsSoftDeletedPath_IsAutoRestoredKeepingItsContentAsync()
    {
        var fx = new TestFixture();
        var mov = Row(MovedRowId, "2024/live.mov", "0ldm0v", softDeleted: true);

        var reconciled = await fx.ReconcileAsync(new FileClassification(FileOutcome.AutoRestoredContentChanged, mov), "2024/live.mov");

        reconciled.Should().BeTrue();
        mov.Should().BeEquivalentTo(new { IsSoftDeleted = false, LastSeenOnNas = Now, ContentHash = "0ldm0v" });
        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((0, 1));
    }

    // ==========================================================================
    // Outcomes that change nothing
    // ==========================================================================

    [Fact]
    public async Task ReconcileAsync_ContentChangedMov_ChangesNothingAsync()
    {
        var fx = new TestFixture();
        var mov = Row(MovedRowId, "2024/live.mov", "0ldm0v");

        var reconciled = await fx.ReconcileAsync(new FileClassification(FileOutcome.ContentChanged, mov), "2024/live.mov");

        reconciled.Should().BeFalse();
        mov.LastSeenOnNas.Should().Be(FirstSeen);
        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((0, 0));
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
    public async Task ReconcileAsync_PathTaken_ChangesNothingAndWarnsAsync()
    {
        var fx = new TestFixture();
        var stored = Row(MovedRowId, "2023/live.mov", "cafebabe");

        (await fx.ReconcileAsync(new FileClassification(FileOutcome.PathTaken, stored), "2024/live.mov")).Should().BeFalse();
        stored.FilePath.Should().Be("2023/live.mov");
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle().Which.Should().Match<FakeLogRecord>(r => r.Level == LogLevel.Warning
            && r.Message == "Cannot record 2024/live.mov: another asset row already holds that path, skipping.");
    }
}
