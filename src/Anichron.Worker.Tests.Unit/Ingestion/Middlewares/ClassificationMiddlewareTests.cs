using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using NSubstitute.ExceptionExtensions;
using static Anichron.Worker.Tests.Unit.Ingestion.Middlewares.ClassificationFixture;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class ClassificationMiddlewareTests
{
    private static MediaAsset Stored(string filePath, string contentHash = "abc123") => new()
    {
        Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        StorageConfigId = ConfigId,
        FilePath = filePath,
        FileName = Path.GetFileName(filePath),
        ContentHash = contentHash,
        LastSeenOnNas = FirstSeen,
    };

    private static MediaAsset StoredMov(string filePath) => new()
    {
        Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        StorageConfigId = ConfigId,
        FilePath = filePath,
        FileName = Path.GetFileName(filePath),
        ContentHash = "cafebabe",
        LastSeenOnNas = FirstSeen,
    };

    private static async Task<bool> NextCalledAsync(ClassificationFixture fx, IngestionContext context)
    {
        var nextCalled = false;
        await fx.Build().InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);
        return nextCalled;
    }

    // ==========================================================================
    // New content
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_UnknownHash_CallsNextWithTheClassificationAndSavesNothingAsync()
    {
        var fx = new ClassificationFixture();
        var context = Single();

        (await NextCalledAsync(fx, context)).Should().BeTrue();

        context.Classification.Should().NotBeNull();
        context.Classification.Primary.Outcome.Should().Be(FileOutcome.New);
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_KnownHashInDifferentConfig_CallsNextAsync()
    {
        var fx = new ClassificationFixture();
        fx.Repository.FindByHashesOrPathsAsync(
                Arg.Is<Guid>(id => id != ConfigId), Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new MatchedAsset(Stored("photo.jpg"), IsLivePhotoVideo: false)]);

        (await NextCalledAsync(fx, Single())).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_UnknownHash_OwnsTheProxiesUnderTheAttemptIdAsync()
    {
        var fx = new ClassificationFixture();
        var context = Single();

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.AssetId.Should().Be(context.AttemptId);
    }

    // ==========================================================================
    // Content changed at the same path
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_EditedFile_CallsNextWithTheStoredRowAsTheOwnerAsync()
    {
        var fx = new ClassificationFixture();
        var stored = Stored("photo.jpg", contentHash: "f00d");
        fx.Store(stored);
        var context = Single("photo.jpg");

        (await NextCalledAsync(fx, context)).Should().BeTrue();

        context.Classification!.Primary.Should().Be(new FileClassification(FileOutcome.ContentChanged, stored));
        context.AssetId.Should().Be(stored.Id);
        context.AttemptId.Should().NotBe(stored.Id);
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_VideoEditedInPlace_ItsRowTakesTheNewContentAndANewVideoProxyAsync()
    {
        var fx = new ClassificationFixture();
        var heic = Stored("photo.heic");
        heic.DateCaptured = new LocalDateTime(2023, 6, 15, 10, 0);
        var mov = StoredMov("photo.mov");
        mov.ContentHash = "0ldm0v";
        var webVideo = new ProxyFile { AssetId = mov.Id, ProxyType = ProxyType.WebVideo, ProxyPath = "0l/old/video_720p.mp4" };
        mov.ProxyFiles = [webVideo];
        heic.PairedAssetId = mov.Id;
        fx.Store(heic, mov);
        var context = Pair();

        (await NextCalledAsync(fx, context)).Should().BeFalse();

        heic.LastSeenOnNas.Should().Be(Now);
        mov.Should().BeEquivalentTo(new
        {
            ContentHash = "cafebabe",
            DateCaptured = new LocalDateTime(2023, 6, 15, 10, 0),
            MediaType = MediaType.Video,
            LastSeenOnNas = Now,
        });
        webVideo.ProxyPath.Should().Be("ca/11111111111111111111111111111111-cafebabe/video_720p.mp4");
        await fx.MovGenerator.Received(1).TranscodeAsync("/abs/photo.mov", Arg.Any<string>(), Arg.Any<CancellationToken>());
        context.ProxyFiles.Should().ContainSingle().Which.AssetId.Should().Be(mov.Id);
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_KnownHash_DoesNotCallNextAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(Stored("photo.jpg"));

        (await NextCalledAsync(fx, Single())).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_SamePath_RefreshesLastSeenOnNasOnlyAsync()
    {
        var fx = new ClassificationFixture();
        var stored = Stored("2023/photo.jpg");
        fx.Store(stored);

        await fx.Build().InvokeAsync(Single("2023/photo.jpg"), NoOpNextAsync, CancellationToken.None);

        stored.LastSeenOnNas.Should().Be(Now);
        stored.FilePath.Should().Be("2023/photo.jpg");
        stored.FileName.Should().Be("photo.jpg");
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_StoredPathGone_MovesRowToNewPathAsync()
    {
        var fx = new ClassificationFixture();
        var stored = Stored("2023/photo.jpg");
        fx.Store(stored);

        await fx.Build().InvokeAsync(Single("2024/renamed.jpg"), NoOpNextAsync, CancellationToken.None);

        stored.Id.Should().Be(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        stored.FilePath.Should().Be("2024/renamed.jpg");
        stored.FileName.Should().Be("renamed.jpg");
        stored.LastSeenOnNas.Should().Be(Now);
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information)
            .Which.Message.Should().Be("Asset eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee moved from 2023/photo.jpg to 2024/renamed.jpg");
    }

    [Fact]
    public async Task InvokeAsync_StoredPathStillExists_LeavesRowUnchangedAsync()
    {
        var fx = new ClassificationFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/photo.jpg");
        var stored = Stored("2023/photo.jpg");
        fx.Store(stored);

        var context = Single("copy/photo.jpg");
        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        stored.FilePath.Should().Be("2023/photo.jpg");
        stored.FileName.Should().Be("photo.jpg");
        stored.LastSeenOnNas.Should().Be(FirstSeen);
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle()
            .Which.Should().Match<FakeLogRecord>(r => r.Level == LogLevel.Debug && r.Message
                == "Duplicate content abc123 in storage config 11111111-1111-1111-1111-111111111111: copy/photo.jpg is a copy of 2023/photo.jpg.");
        context.Classification!.DuplicateCopies.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_Moved_KeepsProxiesAndInteractionsOnTheSameRowAsync()
    {
        var fx = new ClassificationFixture();
        var proxy = new ProxyFile { ProxyType = ProxyType.Thumbnail, ProxyPath = "ab/x-abc123/thumbnail.jpg" };
        var interaction = new AssetInteraction { IsStarred = true };
        var stored = Stored("2023/photo.jpg");
        stored.ProxyFiles = [proxy];
        stored.Interactions = [interaction];
        fx.Store(stored);

        await fx.Build().InvokeAsync(Single("2024/renamed.jpg"), NoOpNextAsync, CancellationToken.None);

        stored.FilePath.Should().Be("2024/renamed.jpg");
        stored.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(proxy);
        proxy.ProxyPath.Should().Be("ab/x-abc123/thumbnail.jpg");
        stored.Interactions.Should().ContainSingle().Which.Should().BeSameAs(interaction);
        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
    }

    private static readonly Guid PathRowId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static MediaAsset PathRow(string filePath, string contentHash, bool softDeleted = false)
    {
        var row = Stored(filePath, contentHash);
        row.Id = PathRowId;
        row.IsSoftDeleted = softDeleted;
        return row;
    }

    [Fact]
    public async Task InvokeAsync_MoveOntoPathHeldByAnotherRow_ThePathsRowTakesTheContentAndTheMovedRowIsRetiredFirstAsync()
    {
        var fx = new ClassificationFixture();
        var moved = Stored("2023/photo.jpg");
        var atPath = PathRow("2024/taken.jpg", "f00d");
        fx.Store(moved, atPath);
        var context = Single("2024/taken.jpg");

        (await NextCalledAsync(fx, context)).Should().BeFalse();

        atPath.Should().BeEquivalentTo(new { ContentHash = "abc123", FilePath = "2024/taken.jpg", IsSoftDeleted = false });
        moved.Should().BeEquivalentTo(new { IsSoftDeleted = true, FilePath = "2023/photo.jpg" });
        context.AssetId.Should().Be(PathRowId);
        await fx.Repository.Received(1).SoftDeleteAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { moved.Id })), Arg.Any<CancellationToken>());
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        (context.SoftDeleted, context.AutoRestored).Should().Be((1, 0));
    }

    [Fact]
    public async Task InvokeAsync_MoveOntoSoftDeletedPath_AutoRestoresThePathsRowWithTheContentAsync()
    {
        var fx = new ClassificationFixture();
        var atPath = PathRow("2024/taken.jpg", "f00d", softDeleted: true);
        fx.Store(Stored("2023/photo.jpg"), atPath);
        var context = Single("2024/taken.jpg");

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        atPath.Should().BeEquivalentTo(new { ContentHash = "abc123", IsSoftDeleted = false });
        (context.SoftDeleted, context.AutoRestored).Should().Be((1, 1));
    }

    [Fact]
    public async Task InvokeAsync_MoveOntoPathWhileAnotherConsumerTakesTheContent_SkipsAndCountsNothingAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(Stored("2023/photo.jpg"), PathRow("2024/taken.jpg", "f00d", softDeleted: true));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        var context = Single("2024/taken.jpg");

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        (context.SoftDeleted, context.AutoRestored).Should().Be((0, 0));
        fx.MiddlewareLogger.Collector.GetSnapshot().Should().ContainSingle().Which.Message.Should().Be(
            "Another consumer took the content now at 2024/taken.jpg first, skipping; the next crawl reconciles it.");
    }

    [Fact]
    public async Task InvokeAsync_CopyOverPathHeldByAnotherRow_SoftDeletesThePathsRowAndCountsTheCopyAsync()
    {
        var fx = new ClassificationFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/photo.jpg");
        var copied = Stored("2023/photo.jpg");
        var atPath = PathRow("copy/photo.jpg", "f00d");
        fx.Store(copied, atPath);
        var context = Single("copy/photo.jpg");

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        atPath.IsSoftDeleted.Should().BeTrue();
        copied.IsSoftDeleted.Should().BeFalse();
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await fx.Repository.DidNotReceiveWithAnyArgs().SoftDeleteAsync(default!, TestContext.Current.CancellationToken);
        context.Classification!.DuplicateCopies.Should().Be(1);
        context.SoftDeleted.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_SoftDeletedRowAtThePath_IsAutoRestoredAndSavedAsync()
    {
        var fx = new ClassificationFixture();
        var trashed = PathRow("2023/photo.jpg", "abc123", softDeleted: true);
        fx.Store(trashed);
        var context = Single("2023/photo.jpg");

        (await NextCalledAsync(fx, context)).Should().BeFalse();

        trashed.Should().BeEquivalentTo(new { IsSoftDeleted = false, LastSeenOnNas = Now });
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        context.AutoRestored.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_NewHeicWithItsMovSoftDeletedAtItsPath_RestoresTheMovBeforeProcessingAsync()
    {
        var fx = new ClassificationFixture();
        var mov = StoredMov("photo.mov");
        mov.IsSoftDeleted = true;
        fx.Store(mov);
        var context = Pair();
        bool? movActiveWhenProcessed = null;

        await fx.Build().InvokeAsync(context, (_, _) =>
        {
            movActiveWhenProcessed = !mov.IsSoftDeleted;
            return Task.CompletedTask;
        }, CancellationToken.None);

        movActiveWhenProcessed.Should().BeTrue();
        context.StagedAutoRestored.Should().Be(1);
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_PathTakenConcurrentlyBeforeTheSave_FailsTheItemAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(Stored("2023/photo.jpg"));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.FilePathUnique));

        var act = () => fx.Build().InvokeAsync(Single("2024/renamed.jpg"), NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task InvokeAsync_SaveWithNothingRetiredFirstLosesARace_FailsTheItemAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(Stored("2023/photo.jpg"));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));

        var act = () => fx.Build().InvokeAsync(Single("2024/renamed.jpg"), NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        await fx.UnitOfWork.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CanInvoke_AnyItem_IsTrue()
        => new ClassificationFixture().Build().CanInvoke(Single()).Should().BeTrue();

    // ==========================================================================
    // Known content — both halves of a Live Photo
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_LivePhotoPairMoved_MovesBothHalvesInOneSaveAsync()
    {
        var fx = new ClassificationFixture();
        var heic = Stored("2023/live.heic");
        var mov = StoredMov("2023/live.mov");
        heic.PairedAssetId = mov.Id;
        mov.ProxyFiles = [new ProxyFile { ProxyType = ProxyType.WebVideo }];
        fx.Store(heic, mov);

        await fx.Build().InvokeAsync(Pair("2024/live.heic", "2024/live.mov"), NoOpNextAsync, CancellationToken.None);

        heic.FilePath.Should().Be("2024/live.heic");
        mov.FilePath.Should().Be("2024/live.mov");
        mov.FileName.Should().Be("live.mov");
        mov.LastSeenOnNas.Should().Be(Now);
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoMovedOntoAStillPhoto_TheStillPhotoTakesThePhotoAndTheMovedVideoAsync()
    {
        var fx = new ClassificationFixture();
        var heic = Stored("2023/live.heic");
        var mov = StoredMov("2023/live.mov");
        var atPath = PathRow("2024/live.heic", "f00d");
        heic.PairedAssetId = mov.Id;
        fx.Store(heic, mov, atPath);

        await fx.Build().InvokeAsync(Pair("2024/live.heic", "2024/live.mov"), NoOpNextAsync, CancellationToken.None);

        atPath.ContentHash.Should().Be("abc123");
        atPath.PairedAssetId.Should().Be(mov.Id);
        (heic.IsSoftDeleted, heic.PairedAssetId).Should().Be((true, null));
        mov.FilePath.Should().Be("2024/live.mov");
        await fx.Repository.Received(1).SoftDeleteAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { heic.Id })), Arg.Any<CancellationToken>());
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await fx.UnitOfWork.Received(1).ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_VideoMovedOntoAPathAnotherRowHolds_ThatRowTakesItAndThePhotoPointsAtItInOneSaveAsync()
    {
        var fx = new ClassificationFixture();
        var heic = Stored("2023/live.heic");
        var mov = StoredMov("2023/live.mov");
        var holder = PathRow("2024/live.mov", "f00d");
        heic.PairedAssetId = mov.Id;
        fx.Store(heic, mov, holder);

        await fx.Build().InvokeAsync(Pair("2024/live.heic", "2024/live.mov"), NoOpNextAsync, CancellationToken.None);

        heic.FilePath.Should().Be("2024/live.heic");
        heic.PairedAssetId.Should().Be(holder.Id);
        holder.ContentHash.Should().Be("cafebabe");
        mov.IsSoftDeleted.Should().BeTrue();
        await fx.MovGenerator.DidNotReceive().TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fx.Repository.Received(1).SoftDeleteAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { mov.Id })), Arg.Any<CancellationToken>());
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairUnchanged_RefreshesBothHalvesAsync()
    {
        var fx = new ClassificationFixture();
        var heic = Stored("2023/live.heic");
        var mov = StoredMov("2023/live.mov");
        heic.PairedAssetId = Guid.NewGuid();
        fx.Store(heic, mov);

        await fx.Build().InvokeAsync(Pair("2023/live.heic", "2023/live.mov"), NoOpNextAsync, CancellationToken.None);

        heic.LastSeenOnNas.Should().Be(Now);
        mov.LastSeenOnNas.Should().Be(Now);
    }

    [Fact]
    public async Task InvokeAsync_HeicDuplicateButMovMoved_SavesMovMoveAsync()
    {
        var fx = new ClassificationFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/live.heic");
        var heic = Stored("2023/live.heic");
        var mov = StoredMov("2023/live.mov");
        heic.PairedAssetId = Guid.NewGuid();
        fx.Store(heic, mov);

        await fx.Build().InvokeAsync(Pair("copy/live.heic", "copy/live.mov"), NoOpNextAsync, CancellationToken.None);

        heic.FilePath.Should().Be("2023/live.heic");
        mov.FilePath.Should().Be("copy/live.mov");
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_BothHalvesDuplicates_SavesNothingAsync()
    {
        var fx = new ClassificationFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/live.heic");
        fx.FileSystem.AddEmptyFile("/abs/2023/live.mov");
        var heic = Stored("2023/live.heic");
        heic.PairedAssetId = Guid.NewGuid();
        fx.Store(heic, StoredMov("2023/live.mov"));

        await fx.Build().InvokeAsync(Pair("copy/live.heic", "copy/live.mov"), NoOpNextAsync, CancellationToken.None);

        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_BothHalvesDuplicates_ReportsEachHalfAsync()
    {
        var fx = new ClassificationFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/live.heic");
        fx.FileSystem.AddEmptyFile("/abs/2023/live.mov");
        var heic = Stored("2023/live.heic");
        heic.PairedAssetId = Guid.NewGuid();
        fx.Store(heic, StoredMov("2023/live.mov"));

        var context = Pair("copy/live.heic", "copy/live.mov");
        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.Classification!.DuplicateCopies.Should().Be(2);
        fx.Logger.Collector.GetSnapshot().Select(r => r.Message)
            .Should().Equal(
                "Duplicate content abc123 in storage config 11111111-1111-1111-1111-111111111111: copy/live.heic is a copy of 2023/live.heic.",
                "Duplicate content cafebabe in storage config 11111111-1111-1111-1111-111111111111: copy/live.mov is a copy of 2023/live.mov.");
    }

    // ==========================================================================
    // A Live Photo as one unit
    // ==========================================================================

    private static readonly Guid AHeicId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000000");
    private static readonly Guid AVideoId = Guid.Parse("a2a2a2a2-0000-0000-0000-000000000000");
    private static readonly Guid BHeicId = Guid.Parse("b1b1b1b1-0000-0000-0000-000000000000");
    private static readonly Guid BVideoId = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000000");

    private static MediaAsset Half(Guid id, string filePath, string contentHash, Guid? pairedAssetId = null) => new()
    {
        Id = id,
        StorageConfigId = ConfigId,
        FilePath = filePath,
        FileName = Path.GetFileName(filePath),
        ContentHash = contentHash,
        MediaType = pairedAssetId is null ? MediaType.Video : MediaType.LivePhoto,
        PairedAssetId = pairedAssetId,
        LastSeenOnNas = FirstSeen,
    };

    private static IngestionContext LivePhoto(string name) => new()
    {
        Item = new LivePhotoPairItem($"/abs/{name}.heic", $"{name}.heic", $"/abs/{name}.mov", $"{name}.mov"),
        Config = new UserStorageConfig { Id = ConfigId, UserId = Guid.NewGuid(), RootPath = "/abs" },
        AttemptId = Guid.NewGuid(),
        ContentHash = "b0b0",
        SecondaryHash = "b1b1",
    };

    private static Task SoftDeletedAsync(ClassificationFixture fx, params Guid[] ids)
        => fx.Repository.Received(1).SoftDeleteAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(retired => retired.SequenceEqual(ids)), Arg.Any<CancellationToken>());

    [Fact]
    public async Task InvokeAsync_WholeLivePhotoMovedOntoAnother_BothOfItsRowsTakeTheContentInOneTransactionAsync()
    {
        var fx = new ClassificationFixture();
        var (aHeic, aVideo) = (Half(AHeicId, "A.heic", "a0a0", AVideoId), Half(AVideoId, "A.mov", "a1a1"));
        var (bHeic, bVideo) = (Half(BHeicId, "B.heic", "b0b0", BVideoId), Half(BVideoId, "B.mov", "b1b1"));
        bVideo.ProxyFiles = [new ProxyFile { AssetId = BVideoId, ProxyType = ProxyType.WebVideo, ProxyPath = "b1/b/video_720p.mp4" }];
        fx.StoreMatched(
            new MatchedAsset(aHeic, IsLivePhotoVideo: false), new MatchedAsset(aVideo, IsLivePhotoVideo: true),
            new MatchedAsset(bHeic, IsLivePhotoVideo: false), new MatchedAsset(bVideo, IsLivePhotoVideo: true));
        var context = LivePhoto("A");

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        (aHeic.ContentHash, aVideo.ContentHash).Should().Be(("b0b0", "b1b1"));
        aHeic.PairedAssetId.Should().Be(AVideoId);
        (bHeic.IsSoftDeleted, bVideo.IsSoftDeleted).Should().Be((true, true));
        bHeic.PairedAssetId.Should().Be(BVideoId);
        fx.AddedProxies.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { AssetId = AVideoId, ProxyType = ProxyType.WebVideo, ProxyPath = "b1/b/video_720p.mp4" });
        await fx.DidNotTranscodeAsync();
        await SoftDeletedAsync(fx, BHeicId, BVideoId);
        await fx.UnitOfWork.Received(1).ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        context.SoftDeleted.Should().Be(2);
    }

    [Fact]
    public async Task InvokeAsync_WholeLivePhotoMovedOntoAnotherLosesTheRace_SavesNothingAndCountsNothingAsync()
    {
        var fx = new ClassificationFixture();
        fx.StoreMatched(
            new MatchedAsset(Half(AHeicId, "A.heic", "a0a0", AVideoId), IsLivePhotoVideo: false),
            new MatchedAsset(Half(AVideoId, "A.mov", "a1a1"), IsLivePhotoVideo: true),
            new MatchedAsset(Half(BHeicId, "B.heic", "b0b0", BVideoId), IsLivePhotoVideo: false),
            new MatchedAsset(Half(BVideoId, "B.mov", "b1b1"), IsLivePhotoVideo: true));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        var context = LivePhoto("A");

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        context.SoftDeleted.Should().Be(0);
        fx.MiddlewareLogger.Collector.GetSnapshot().Should().ContainSingle()
            .Which.Message.Should().Contain("A.heic");
    }

    [Fact]
    public async Task InvokeAsync_AnotherLivePhotosVideoCopiedOverTheVideo_RetiresThePairedRowAndCountsTheCopyAsync()
    {
        var fx = new ClassificationFixture();
        fx.FileSystem.AddEmptyFile("/abs/B.mov");
        var (aHeic, aVideo) = (Half(AHeicId, "A.heic", "b0b0", AVideoId), Half(AVideoId, "A.mov", "a1a1"));
        var bVideo = Half(BVideoId, "B.mov", "b1b1");
        fx.StoreMatched(
            new MatchedAsset(aHeic, IsLivePhotoVideo: false),
            new MatchedAsset(aVideo, IsLivePhotoVideo: true),
            new MatchedAsset(bVideo, IsLivePhotoVideo: true));
        var context = LivePhoto("A");

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        aVideo.IsSoftDeleted.Should().BeTrue();
        (bVideo.IsSoftDeleted, bVideo.FilePath).Should().Be((false, "B.mov"));
        aHeic.PairedAssetId.Should().Be(AVideoId);
        context.Classification!.DuplicateCopies.Should().Be(1);
        context.SoftDeleted.Should().Be(1);
        await fx.UnitOfWork.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AnotherLivePhotosVideoMovedOntoTheVideo_ThePairedRowTakesItAndTheMovedRowIsRetiredFirstAsync()
    {
        var fx = new ClassificationFixture();
        var (aHeic, aVideo) = (Half(AHeicId, "A.heic", "b0b0", AVideoId), Half(AVideoId, "A.mov", "a1a1"));
        var bVideo = Half(BVideoId, "B.mov", "b1b1");
        fx.StoreMatched(
            new MatchedAsset(aHeic, IsLivePhotoVideo: false),
            new MatchedAsset(aVideo, IsLivePhotoVideo: true),
            new MatchedAsset(bVideo, IsLivePhotoVideo: true));

        await fx.Build().InvokeAsync(LivePhoto("A"), NoOpNextAsync, CancellationToken.None);

        (aVideo.ContentHash, aVideo.IsSoftDeleted).Should().Be(("b1b1", false));
        bVideo.IsSoftDeleted.Should().BeTrue();
        await SoftDeletedAsync(fx, BVideoId);
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoCopiedOverAnother_LeavesTheCopiedRowsAndRetiresOnlyTheOverwrittenPhotoAsync()
    {
        var fx = new ClassificationFixture();
        fx.FileSystem.AddEmptyFile("/abs/B.heic");
        var (aHeic, aVideo) = (Half(AHeicId, "A.heic", "a0a0", AVideoId), Half(AVideoId, "A.mov", "a1a1"));
        var bHeic = Half(BHeicId, "B.heic", "b0b0", BVideoId);
        fx.StoreMatched(
            new MatchedAsset(aHeic, IsLivePhotoVideo: false),
            new MatchedAsset(aVideo, IsLivePhotoVideo: true),
            new MatchedAsset(bHeic, IsLivePhotoVideo: false));

        await fx.Build().InvokeAsync(LivePhoto("A"), NoOpNextAsync, CancellationToken.None);

        aHeic.IsSoftDeleted.Should().BeTrue();
        (aVideo.IsSoftDeleted, aVideo.LastSeenOnNas).Should().Be((false, FirstSeen));
        (bHeic.PairedAssetId, bHeic.FilePath, bHeic.LastSeenOnNas).Should().Be((BVideoId, "B.heic", FirstSeen));
        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
    }

    [Fact]
    public async Task InvokeAsync_PhotoAndVideoEditedTogether_TheVideoTakesItsContentBeforeThePhotoIsProcessedAsync()
    {
        var fx = new ClassificationFixture();
        var (aHeic, aVideo) = (Half(AHeicId, "A.heic", "a0a0", AVideoId), Half(AVideoId, "A.mov", "a1a1"));
        fx.Store(aHeic, aVideo);
        string? videoHashAtNext = null;

        await fx.Build().InvokeAsync(LivePhoto("A"), (_, _) =>
        {
            videoHashAtNext = aVideo.ContentHash;
            return Task.CompletedTask;
        }, CancellationToken.None);

        videoHashAtNext.Should().Be("b1b1");
        await fx.MovGenerator.Received(1).TranscodeAsync("/abs/A.mov", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
