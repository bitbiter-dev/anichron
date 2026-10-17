using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using Npgsql;
using NSubstitute.ExceptionExtensions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class IdempotencyCheckMiddlewareTests
{
    private static readonly Instant FirstSeen = Instant.FromUtc(2025, 1, 1, 0, 0, 0);
    private static readonly Instant Now = Instant.FromUtc(2026, 5, 19, 10, 0, 0);

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public MockFileSystem FileSystem { get; } = new();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public FakeLogger<IdempotencyCheckMiddleware> Logger { get; } = new();

        public TestFixture() => Clock.GetCurrentInstant().Returns(Now);

        public IdempotencyCheckMiddleware Build()
            => new(Repository, UnitOfWork, FileSystem, Clock, Logger);
    }

    private static IngestionContext MakeContext(string? contentHash = null, string relativePath = "photo.jpg") => new()
    {
        Item = new SingleFileItem($"/abs/{relativePath}", relativePath, MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        AssetId = Guid.NewGuid(),
        ContentHash = contentHash,
    };

    private static MediaAsset Stored(string filePath) => new()
    {
        Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        FilePath = filePath,
        FileName = Path.GetFileName(filePath),
        ContentHash = "abc123",
        LastSeenOnNas = FirstSeen,
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
    public async Task InvokeAsync_KnownHash_DoesNotCallNextAsync()
    {
        var fx = new TestFixture();
        fx.Repository.FindByHashAsync("abc123", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Stored("photo.jpg"));
        var nextCalled = false;
        Task NextAsync(IngestionContext ctx, CancellationToken ct)
        {
            _ = (ctx, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await fx.Build().InvokeAsync(MakeContext("abc123"), NextAsync, CancellationToken.None);

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_UnknownHash_CallsNextAsync()
    {
        var fx = new TestFixture();
        fx.Repository.FindByHashAsync("abc123", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((MediaAsset?)null);
        var nextCalled = false;
        Task NextAsync(IngestionContext ctx, CancellationToken ct)
        {
            _ = (ctx, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await fx.Build().InvokeAsync(MakeContext("abc123"), NextAsync, CancellationToken.None);

        nextCalled.Should().BeTrue();
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_KnownHashInDifferentConfig_CallsNextAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext("abc123");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>())
            .Returns((MediaAsset?)null);
        fx.Repository.FindByHashAsync("abc123", Arg.Is<Guid>(id => id != context.Config.Id), Arg.Any<CancellationToken>())
            .Returns(Stored("photo.jpg"));
        var nextCalled = false;
        Task NextAsync(IngestionContext ctx, CancellationToken ct)
        {
            _ = (ctx, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await fx.Build().InvokeAsync(context, NextAsync, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_SamePath_RefreshesLastSeenOnNasOnlyAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext("abc123", "2023/photo.jpg");
        fx.FileSystem.AddEmptyFile("/abs/2023/photo.jpg");
        var stored = Stored("2023/photo.jpg");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(stored);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        stored.LastSeenOnNas.Should().Be(Now);
        stored.FilePath.Should().Be("2023/photo.jpg");
        stored.FileName.Should().Be("photo.jpg");
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_StoredPathGone_MovesRowToNewPathAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext("abc123", "2024/renamed.jpg");
        fx.FileSystem.AddEmptyFile("/abs/2024/renamed.jpg");
        var stored = Stored("2023/photo.jpg");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(stored);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        stored.Id.Should().Be(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        stored.FilePath.Should().Be("2024/renamed.jpg");
        stored.FileName.Should().Be("renamed.jpg");
        stored.LastSeenOnNas.Should().Be(Now);
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_StoredPathStillExists_LeavesRowUnchangedAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext("abc123", "copy/photo.jpg");
        fx.FileSystem.AddEmptyFile("/abs/copy/photo.jpg");
        fx.FileSystem.AddEmptyFile("/abs/2023/photo.jpg");
        var stored = Stored("2023/photo.jpg");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(stored);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        stored.FilePath.Should().Be("2023/photo.jpg");
        stored.FileName.Should().Be("photo.jpg");
        stored.LastSeenOnNas.Should().Be(FirstSeen);
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle()
            .Which.Should().Match<FakeLogRecord>(r => r.Level == LogLevel.Debug && r.Message == "copy/photo.jpg is a copy of already-ingested 2023/photo.jpg, skipping.");
    }

    [Fact]
    public async Task InvokeAsync_Moved_KeepsProxiesAndInteractionsOnTheSameRowAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext("abc123", "2024/renamed.jpg");
        var proxy = new ProxyFile { ProxyType = ProxyType.Thumbnail, ProxyPath = "ab/x-abc123/thumbnail.jpg" };
        var interaction = new AssetInteraction { IsStarred = true };
        var stored = Stored("2023/photo.jpg");
        stored.ProxyFiles = [proxy];
        stored.Interactions = [interaction];
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(stored);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        stored.FilePath.Should().Be("2024/renamed.jpg");
        stored.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(proxy);
        proxy.ProxyPath.Should().Be("ab/x-abc123/thumbnail.jpg");
        stored.Interactions.Should().ContainSingle().Which.Should().BeSameAs(interaction);
        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
    }

    [Fact]
    public async Task InvokeAsync_MoveOntoPathHeldByAnotherRow_RestoresRowAndWarnsAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext("abc123", "2024/taken.jpg");
        var stored = Stored("2023/photo.jpg");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(stored);
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(UniqueViolation("IX_MediaAssets_StorageConfigId_FilePath"));

        var act = () => fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await act.Should().NotThrowAsync();
        stored.FilePath.Should().Be("2023/photo.jpg");
        stored.FileName.Should().Be("photo.jpg");
        stored.LastSeenOnNas.Should().Be(FirstSeen);
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Be("Cannot record 2024/taken.jpg: another asset row already holds that path, skipping.");
    }

    [Fact]
    public async Task InvokeAsync_OtherUniqueViolation_RethrowsAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext("abc123", "2024/renamed.jpg");
        fx.FileSystem.AddEmptyFile("/abs/2024/renamed.jpg");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>())
            .Returns(Stored("2023/photo.jpg"));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));

        var act = () => fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task InvokeAsync_NonPostgresUpdateFailure_RethrowsAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext("abc123", "2024/renamed.jpg");
        fx.FileSystem.AddEmptyFile("/abs/2024/renamed.jpg");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>())
            .Returns(Stored("2023/photo.jpg"));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("boom"));

        var act = () => fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairMoved_MovesBothHalvesAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext("2024/live.heic", "2024/live.mov");
        fx.FileSystem.AddEmptyFile("/abs/2024/live.heic");
        fx.FileSystem.AddEmptyFile("/abs/2024/live.mov");
        var heic = Stored("2023/live.heic");
        var mov = Stored("2023/live.mov");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(heic);
        fx.Repository.FindByHashAsync("cafebabe", context.Config.Id, Arg.Any<CancellationToken>()).Returns(mov);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        heic.FilePath.Should().Be("2024/live.heic");
        mov.FilePath.Should().Be("2024/live.mov");
        mov.FileName.Should().Be("live.mov");
        mov.LastSeenOnNas.Should().Be(Now);
        await fx.UnitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_HeicMoveClashes_StillMovesMovAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext("2024/live.heic", "2024/live.mov");
        var heic = Stored("2023/live.heic");
        var mov = Stored("2023/live.mov");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(heic);
        fx.Repository.FindByHashAsync("cafebabe", context.Config.Id, Arg.Any<CancellationToken>()).Returns(mov);
        string? heicPathAtSecondSave = null;
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromException<int>(UniqueViolation("IX_MediaAssets_StorageConfigId_FilePath")),
            _ =>
            {
                heicPathAtSecondSave = heic.FilePath;
                return Task.FromResult(1);
            });

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        heicPathAtSecondSave.Should().Be("2023/live.heic");
        mov.FilePath.Should().Be("2024/live.mov");
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("2024/live.heic");
    }

    [Fact]
    public async Task InvokeAsync_MovMoveClashes_KeepsHeicMoveAndNamesMovPathAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext("2024/live.heic", "2024/live.mov");
        var heic = Stored("2023/live.heic");
        var mov = Stored("2023/live.mov");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(heic);
        fx.Repository.FindByHashAsync("cafebabe", context.Config.Id, Arg.Any<CancellationToken>()).Returns(mov);
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(1),
            _ => Task.FromException<int>(UniqueViolation("IX_MediaAssets_StorageConfigId_FilePath")));

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        heic.FilePath.Should().Be("2024/live.heic");
        mov.FilePath.Should().Be("2023/live.mov");
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("2024/live.mov");
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairUnchanged_RefreshesBothHalvesAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext("2023/live.heic", "2023/live.mov");
        var heic = Stored("2023/live.heic");
        var mov = Stored("2023/live.mov");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(heic);
        fx.Repository.FindByHashAsync("cafebabe", context.Config.Id, Arg.Any<CancellationToken>()).Returns(mov);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        heic.LastSeenOnNas.Should().Be(Now);
        mov.LastSeenOnNas.Should().Be(Now);
    }

    [Fact]
    public async Task InvokeAsync_HeicDuplicateButMovMoved_SavesMovMoveAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext("copy/live.heic", "copy/live.mov");
        fx.FileSystem.AddEmptyFile("/abs/2023/live.heic");
        var heic = Stored("2023/live.heic");
        var mov = Stored("2023/live.mov");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(heic);
        fx.Repository.FindByHashAsync("cafebabe", context.Config.Id, Arg.Any<CancellationToken>()).Returns(mov);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        heic.FilePath.Should().Be("2023/live.heic");
        mov.FilePath.Should().Be("copy/live.mov");
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoMovNotStored_TracksHeicOnlyAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext("2023/live.heic", "2023/live.mov");
        var heic = Stored("2023/live.heic");
        fx.Repository.FindByHashAsync("abc123", context.Config.Id, Arg.Any<CancellationToken>()).Returns(heic);
        fx.Repository.FindByHashAsync("cafebabe", context.Config.Id, Arg.Any<CancellationToken>()).Returns((MediaAsset?)null);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        heic.LastSeenOnNas.Should().Be(Now);
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static IngestionContext MakePairContext(string heicPath, string movPath) => new()
    {
        Item = new LivePhotoPairItem($"/abs/{heicPath}", heicPath, $"/abs/{movPath}", movPath),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        AssetId = Guid.NewGuid(),
        ContentHash = "abc123",
        SecondaryHash = "cafebabe",
    };

    private static DbUpdateException UniqueViolation(string constraintName)
        => new("duplicate key", new PostgresException(
            "duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            null, null, 0, 0, null, null, null, null, null, null,
            constraintName, null, null, null));
}
