using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
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
            .Returns([Stored("photo.jpg")]);

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
    public async Task InvokeAsync_UnchangedHeicWithEditedMov_LeavesBothRowsAndSavesOnlyTheHeicRefreshAsync()
    {
        var fx = new ClassificationFixture();
        var heic = Stored("photo.heic");
        var mov = StoredMov("photo.mov");
        mov.ContentHash = "0ldm0v";
        mov.ProxyFiles = [new ProxyFile { AssetId = mov.Id, ProxyType = ProxyType.WebVideo }];
        heic.PairedAssetId = mov.Id;
        fx.Store(heic, mov);

        (await NextCalledAsync(fx, Pair())).Should().BeFalse();

        heic.LastSeenOnNas.Should().Be(Now);
        mov.ContentHash.Should().Be("0ldm0v");
        mov.LastSeenOnNas.Should().Be(FirstSeen);
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Known content
    // ==========================================================================

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

    [Fact]
    public async Task InvokeAsync_MoveOntoPathHeldByAnotherRow_LeavesRowAndWarnsWithoutSavingAsync()
    {
        var fx = new ClassificationFixture();
        var stored = Stored("2023/photo.jpg");
        fx.Store(stored, Stored("2024/taken.jpg", contentHash: "f00d"));

        await fx.Build().InvokeAsync(Single("2024/taken.jpg"), NoOpNextAsync, CancellationToken.None);

        stored.FilePath.Should().Be("2023/photo.jpg");
        stored.FileName.Should().Be("photo.jpg");
        stored.LastSeenOnNas.Should().Be(FirstSeen);
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Be("Cannot record 2024/taken.jpg: another asset row already holds that path, skipping.");
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
    public async Task InvokeAsync_HeicPathTaken_StillMovesMovAsync()
    {
        var fx = new ClassificationFixture();
        var heic = Stored("2023/live.heic");
        var mov = StoredMov("2023/live.mov");
        fx.Store(heic, mov, Stored("2024/live.heic", contentHash: "f00d"));
        heic.PairedAssetId = Guid.NewGuid();

        await fx.Build().InvokeAsync(Pair("2024/live.heic", "2024/live.mov"), NoOpNextAsync, CancellationToken.None);

        heic.FilePath.Should().Be("2023/live.heic");
        mov.FilePath.Should().Be("2024/live.mov");
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("2024/live.heic");
    }

    [Fact]
    public async Task InvokeAsync_MovPathTaken_KeepsHeicMoveAndNamesMovPathAsync()
    {
        var fx = new ClassificationFixture();
        var heic = Stored("2023/live.heic");
        var mov = StoredMov("2023/live.mov");
        fx.Store(heic, mov, Stored("2024/live.mov", contentHash: "f00d"));
        heic.PairedAssetId = Guid.NewGuid();

        await fx.Build().InvokeAsync(Pair("2024/live.heic", "2024/live.mov"), NoOpNextAsync, CancellationToken.None);

        heic.FilePath.Should().Be("2024/live.heic");
        mov.FilePath.Should().Be("2023/live.mov");
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("2024/live.mov");
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
}
