using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NSubstitute.ExceptionExtensions;
using static Anichron.Worker.Tests.Unit.Ingestion.Middlewares.ClassificationFixture;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class ClassificationMiddlewarePairingTests
{
    private static readonly Guid PairedMovId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NewMovId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly LocalDateTime HeicCaptured = new(2023, 6, 15, 10, 0);

    private static MediaAsset StoredHeic(Guid? pairedAssetId = null) => new()
    {
        Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        FilePath = "photo.heic",
        FileName = "photo.heic",
        ContentHash = "abc123",
        MediaType = MediaType.Image,
        DateCaptured = HeicCaptured,
        PairedAssetId = pairedAssetId,
    };

    private static MediaAsset StoredMov(Guid id, params ProxyType[] proxies) => new()
    {
        Id = id,
        FilePath = "photo.mov",
        FileName = "photo.mov",
        ContentHash = "cafebabe",
        MediaType = MediaType.Video,
        ProxyFiles = [.. proxies.Select(type => new ProxyFile { AssetId = id, ProxyType = type })],
    };

    private static void FailSaves(ClassificationFixture fx, Exception ex)
        => fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(ex);

    [Fact]
    public async Task InvokeAsync_StoredHeicWithoutMov_AttachesNewMovAndStopsAsync()
    {
        var fx = new ClassificationFixture();
        var heic = StoredHeic();
        fx.Store(heic);
        fx.GuidFactory.NewGuid().Returns(NewMovId);
        MediaAsset? addedMov = null;
        fx.Repository.When(r => r.Add(Arg.Any<MediaAsset>())).Do(call => addedMov = call.Arg<MediaAsset>());
        var nextCalled = false;

        await fx.Build().InvokeAsync(Pair(), (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        addedMov.Should().NotBeNull();
        addedMov.Id.Should().Be(NewMovId);
        addedMov.ContentHash.Should().Be("cafebabe");
        addedMov.FilePath.Should().Be("photo.mov");
        addedMov.DateCaptured.Should().Be(HeicCaptured);
        heic.MediaType.Should().Be(MediaType.LivePhoto);
        heic.PairedAssetId.Should().Be(NewMovId);
        nextCalled.Should().BeFalse();
        await fx.MovGenerator.Received(1).TranscodeAsync("/abs/photo.mov", Arg.Any<string>(), Arg.Any<CancellationToken>());
        var row = fx.AddedProxies.Should().ContainSingle().Which;
        row.AssetId.Should().Be(NewMovId);
        row.ProxyType.Should().Be(ProxyType.WebVideo);
    }

    [Fact]
    public async Task InvokeAsync_StoredHeicAndStoredMov_PairsToExistingMovWithoutAddingAsync()
    {
        var fx = new ClassificationFixture();
        var heic = StoredHeic();
        var existingMov = StoredMov(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), ProxyType.WebVideo);
        fx.Store(heic, existingMov);

        await fx.Build().InvokeAsync(Pair(), NoOpNextAsync, CancellationToken.None);

        await fx.DidNotTranscodeAsync();
        fx.AddedProxies.Should().BeEmpty();
        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
        heic.PairedAssetId.Should().Be(existingMov.Id);
        heic.MediaType.Should().Be(MediaType.LivePhoto);
    }

    [Fact]
    public async Task InvokeAsync_LatePairing_CommitsBeforeRefreshingTheHalvesAsync()
    {
        var fx = new ClassificationFixture();
        var heic = StoredHeic();
        fx.Store(heic);
        fx.GuidFactory.NewGuid().Returns(NewMovId);
        Guid? pairedAtFirstSave = null;
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(
            _ =>
            {
                pairedAtFirstSave ??= heic.PairedAssetId;
                return Task.FromResult(1);
            });

        await fx.Build().InvokeAsync(Pair(), NoOpNextAsync, CancellationToken.None);

        pairedAtFirstSave.Should().Be(NewMovId);
        await fx.UnitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        heic.LastSeenOnNas.Should().Be(Now);
    }

    [Fact]
    public async Task InvokeAsync_StoredHeicAlreadyPaired_LeavesThePairingUnchangedAsync()
    {
        var fx = new ClassificationFixture();
        var heic = StoredHeic(PairedMovId);
        fx.Store(heic);

        await fx.Build().InvokeAsync(Pair(), NoOpNextAsync, CancellationToken.None);

        heic.PairedAssetId.Should().Be(PairedMovId);
        heic.MediaType.Should().Be(MediaType.Image);
        await fx.DidNotTranscodeAsync();
    }

    [Fact]
    public async Task InvokeAsync_AlreadyPairedMovWithoutVideoProxy_BackfillsItAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(StoredHeic(PairedMovId), StoredMov(PairedMovId));

        await fx.Build().InvokeAsync(Pair(), NoOpNextAsync, CancellationToken.None);

        await fx.MovGenerator.Received(1).TranscodeAsync("/abs/photo.mov", Arg.Any<string>(), Arg.Any<CancellationToken>());
        fx.AddedProxies.Should().ContainSingle().Which.AssetId.Should().Be(PairedMovId);
        await fx.UnitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AlreadyPairedMovWithVideoProxy_NeitherTranscodesNorSavesTheBackfillAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(StoredHeic(PairedMovId), StoredMov(PairedMovId, ProxyType.WebVideo));

        await fx.Build().InvokeAsync(Pair(), NoOpNextAsync, CancellationToken.None);

        await fx.DidNotTranscodeAsync();
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AlreadyPairedToADifferentMovThanThisFile_LeavesItAloneAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(StoredHeic(PairedMovId), StoredMov(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")));

        await fx.Build().InvokeAsync(Pair(), NoOpNextAsync, CancellationToken.None);

        await fx.DidNotTranscodeAsync();
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_PairedHeicWithMovNotStored_RefreshesTheHeicOnlyAsync()
    {
        var fx = new ClassificationFixture();
        var heic = StoredHeic(PairedMovId);
        fx.Store(heic);

        await fx.Build().InvokeAsync(Pair(), NoOpNextAsync, CancellationToken.None);

        heic.LastSeenOnNas.Should().Be(Now);
        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_DuplicateHeicWithMovNotStored_SavesNothingAsync()
    {
        var fx = new ClassificationFixture();
        fx.FileSystem.AddEmptyFile("/abs/photo.heic");
        fx.Store(StoredHeic(PairedMovId));

        await fx.Build().InvokeAsync(Pair("copy/photo.heic", "copy/photo.mov"), NoOpNextAsync, CancellationToken.None);

        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_LatePairingLosesTheRaceForTheMov_ReleasesItsProxiesBeforeRethrowingAsync()
    {
        var fx = new ClassificationFixture();
        fx.GuidFactory.NewGuid().Returns(NewMovId);
        fx.Store(StoredHeic());
        FailSaves(fx, DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        var context = Pair();

        var act = () => fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        context.ProxyFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_BackfillLosesTheRaceForTheProxyRow_ReleasesItsProxiesBeforeRethrowingAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(StoredHeic(PairedMovId), StoredMov(PairedMovId));
        FailSaves(fx, DatabaseErrors.UniqueViolation(ProxyFileIndexNames.OnePerAssetAndType));
        var context = Pair();

        var act = () => fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        context.ProxyFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_BackfillCommittedThenALaterSaveThrows_LeavesTheCommittedProxyToTheDatabaseAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(StoredHeic(PairedMovId), StoredMov(PairedMovId));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(1),
            _ => Task.FromException<int>(new DbUpdateException("later save")));
        var context = Pair();

        var act = () => fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        await fx.UnitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        context.ProxyFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_LatePairingFailsForAnotherReason_KeepsTheProxiesForCompensationAsync()
    {
        var fx = new ClassificationFixture();
        fx.GuidFactory.NewGuid().Returns(NewMovId);
        fx.Store(StoredHeic());
        FailSaves(fx, new DbUpdateException("down"));
        var context = Pair();

        var act = () => fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        context.ProxyFiles.Should().ContainSingle();
    }

    [Fact]
    public async Task InvokeAsync_HeicNotStored_PassesThroughWithoutPairingOrSavingAsync()
    {
        var fx = new ClassificationFixture();
        fx.Store(StoredMov(PairedMovId));
        var nextCalled = false;

        await fx.Build().InvokeAsync(Pair(), (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await fx.DidNotTranscodeAsync();
    }
}
