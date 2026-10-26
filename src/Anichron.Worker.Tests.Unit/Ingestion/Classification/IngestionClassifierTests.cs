using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class IngestionClassifierTests
{
    private static readonly Guid ConfigId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MovId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly string[] SingleHash = ["abc123"];
    private static readonly string[] SinglePath = ["2023/photo.jpg"];
    private static readonly string[] PairHashes = ["abc123", "cafebabe"];
    private static readonly string[] PairPaths = ["2023/live.heic", "2023/live.mov"];

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public MockFileSystem FileSystem { get; } = new();

        public void Store(params MediaAsset[] rows)
            => Repository.FindByHashesOrPathsAsync(
                    ConfigId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(rows);

        public Task<IngestionClassification> ClassifyAsync(IngestionContext context)
            => new IngestionClassifier(Repository, FileSystem).ClassifyAsync(context, CancellationToken.None);
    }

    private static UserStorageConfig Config => new() { Id = ConfigId, UserId = Guid.NewGuid(), RootPath = "/abs" };

    private static IngestionContext Single(string relativePath = "photo.jpg") => new()
    {
        Item = new SingleFileItem($"/abs/{relativePath}", relativePath, MediaType.Image),
        Config = Config,
        AttemptId = Guid.NewGuid(),
        ContentHash = "abc123",
    };

    private static IngestionContext Pair(string heicPath = "live.heic", string movPath = "live.mov") => new()
    {
        Item = new LivePhotoPairItem($"/abs/{heicPath}", heicPath, $"/abs/{movPath}", movPath),
        Config = Config,
        AttemptId = Guid.NewGuid(),
        ContentHash = "abc123",
        SecondaryHash = "cafebabe",
    };

    private static MediaAsset Row(
        string hash, string filePath, bool softDeleted = false, Guid? id = null, Guid? pairedAssetId = null, Instant? lastSeen = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            ContentHash = hash,
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            IsSoftDeleted = softDeleted,
            PairedAssetId = pairedAssetId,
            LastSeenOnNas = lastSeen ?? Instant.FromUtc(2025, 1, 1, 0, 0),
        };

    // ==========================================================================
    // One file
    // ==========================================================================

    [Fact]
    public async Task ClassifyAsync_NoRowHoldsTheContent_IsNewAndNeedsProcessing()
    {
        var fx = new TestFixture();
        fx.Store();

        var result = await fx.ClassifyAsync(Single());

        result.Primary.Should().Be(new FileClassification(FileOutcome.New, null));
        result.NeedsProcessing.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyAsync_RowAtTheSamePath_IsUnchanged()
    {
        var fx = new TestFixture();
        var stored = Row("abc123", "2023/photo.jpg");
        fx.Store(stored);

        var result = await fx.ClassifyAsync(Single("2023/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.Unchanged, stored));
        result.NeedsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_StoredFileGone_IsMoved()
    {
        var fx = new TestFixture();
        var stored = Row("abc123", "2023/photo.jpg");
        fx.Store(stored);

        var result = await fx.ClassifyAsync(Single("2024/renamed.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.Moved, stored));
    }

    [Fact]
    public async Task ClassifyAsync_CopiedOverThePathOfAnActiveRow_IsOverwrittenByCopyRetiringThePathsRow()
    {
        var fx = new TestFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/photo.jpg");
        var stored = Row("abc123", "2023/photo.jpg");
        var atPath = Row("f00d", "copy/photo.jpg");
        fx.Store(stored, atPath);

        var result = await fx.ClassifyAsync(Single("copy/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.OverwrittenByCopy, stored, Retired: atPath));
        result.NeedsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_StoredFileStillThereAndNoRowAtThePath_IsADuplicateCopy()
    {
        var fx = new TestFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/photo.jpg");
        var stored = Row("abc123", "2023/photo.jpg");
        fx.Store(stored);

        var result = await fx.ClassifyAsync(Single("copy/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.DuplicateCopy, stored));
    }

    [Fact]
    public async Task ClassifyAsync_StoredFileStillThereAndASoftDeletedRowAtThePath_IsADuplicateCopy()
    {
        var fx = new TestFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/photo.jpg");
        var stored = Row("abc123", "2023/photo.jpg");
        fx.Store(stored, Row("f00d", "copy/photo.jpg", softDeleted: true));

        var result = await fx.ClassifyAsync(Single("copy/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.DuplicateCopy, stored));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassifyAsync_MovedOntoThePathOfAnotherRow_IsOverwrittenByMoveRetiringTheMovedRow(bool holderSoftDeleted)
    {
        var fx = new TestFixture();
        var stored = Row("abc123", "2023/photo.jpg");
        var atPath = Row("f00d", "2024/taken.jpg", softDeleted: holderSoftDeleted);
        fx.Store(stored, atPath);

        var result = await fx.ClassifyAsync(Single("2024/taken.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.OverwrittenByMove, atPath, Retired: stored));
        result.NeedsProcessing.Should().BeFalse();
    }

    // ==========================================================================
    // Soft-deleted rows
    // ==========================================================================

    [Fact]
    public async Task ClassifyAsync_SoftDeletedRowAtThePathWithTheContent_IsAutoRestored()
    {
        var fx = new TestFixture();
        var trashed = Row("abc123", "2023/photo.jpg", softDeleted: true);
        fx.Store(trashed);

        var result = await fx.ClassifyAsync(Single("2023/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.AutoRestored, trashed));
        result.NeedsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_SoftDeletedRowAtThePathWithOtherContent_IsAutoRestoredContentChangedAndNeedsProcessing()
    {
        var fx = new TestFixture();
        var trashed = Row("f00d", "2023/photo.jpg", softDeleted: true);
        fx.Store(trashed);

        var result = await fx.ClassifyAsync(Single("2023/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.AutoRestoredContentChanged, trashed));
        result.NeedsProcessing.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyAsync_PathHoldsOneSoftDeletedRowAndTheContentAnother_ThePathsRowWins()
    {
        var fx = new TestFixture();
        var atPath = Row("f00d", "2023/photo.jpg", softDeleted: true);
        fx.Store(Row("abc123", "2020/old.jpg", softDeleted: true), atPath);

        var result = await fx.ClassifyAsync(Single("2023/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.AutoRestoredContentChanged, atPath));
    }

    [Fact]
    public async Task ClassifyAsync_ContentOfSoftDeletedRowsElsewhere_AutoRestoresTheMostRecentlySeenOneAtTheNewPath()
    {
        var fx = new TestFixture();
        var latest = Row("abc123", "2022/b.jpg", softDeleted: true, lastSeen: Instant.FromUtc(2025, 6, 1, 0, 0));
        fx.Store(
            Row("abc123", "2021/a.jpg", softDeleted: true, lastSeen: Instant.FromUtc(2025, 5, 1, 0, 0)),
            latest,
            Row("abc123", "2023/c.jpg", softDeleted: true, lastSeen: Instant.FromUtc(2025, 4, 1, 0, 0)));

        var result = await fx.ClassifyAsync(Single("2024/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.AutoRestoredMoved, latest));
        result.NeedsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_ActiveRowWithTheContentAndASoftDeletedOne_FollowsTheActiveRow()
    {
        var fx = new TestFixture();
        var active = Row("abc123", "2023/photo.jpg");
        fx.Store(Row("abc123", "2020/photo.jpg", softDeleted: true), active);

        var result = await fx.ClassifyAsync(Single("2024/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.Moved, active));
    }

    [Fact]
    public async Task ClassifyAsync_UnknownHashAtAPathAnActiveRowHolds_IsContentChangedAndNeedsProcessing()
    {
        var fx = new TestFixture();
        var stored = Row("f00d", "2023/photo.jpg");
        fx.Store(stored);

        var result = await fx.ClassifyAsync(Single("2023/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.ContentChanged, stored));
        result.NeedsProcessing.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyAsync_UnknownHashWhileAnActiveRowHoldsAnotherPath_IsNew()
    {
        var fx = new TestFixture();
        fx.Store(Row("f00d", "2023/other.jpg"));

        var result = await fx.ClassifyAsync(Single("2023/photo.jpg"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.New, null));
    }

    [Fact]
    public async Task ClassifyAsync_SingleFile_HasNoSecondaryAndNoPairing()
    {
        var fx = new TestFixture();
        fx.Store(Row("abc123", "photo.jpg"));

        var result = await fx.ClassifyAsync(Single());

        result.Secondary.Should().BeNull();
        result.Pairing.Should().Be(PairingAction.None);
    }

    [Fact]
    public async Task ClassifyAsync_SingleFile_AsksForItsHashAndPathInItsConfig()
    {
        var fx = new TestFixture();
        fx.Store();

        await fx.ClassifyAsync(Single("2023/photo.jpg"));

        await fx.Repository.Received(1).FindByHashesOrPathsAsync(
            ConfigId,
            Arg.Is<IReadOnlyCollection<string>>(h => h.SequenceEqual(SingleHash)),
            Arg.Is<IReadOnlyCollection<string>>(p => p.SequenceEqual(SinglePath)),
            Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Live Photo pair
    // ==========================================================================

    [Fact]
    public async Task ClassifyAsync_Pair_AsksForBothHashesAndBothPathsInOneQuery()
    {
        var fx = new TestFixture();
        fx.Store();

        await fx.ClassifyAsync(Pair("2023/live.heic", "2023/live.mov"));

        await fx.Repository.Received(1).FindByHashesOrPathsAsync(
            ConfigId,
            Arg.Is<IReadOnlyCollection<string>>(h => h.SequenceEqual(PairHashes)),
            Arg.Is<IReadOnlyCollection<string>>(p => p.SequenceEqual(PairPaths)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_Pair_ClassifiesEachHalfOnItsOwn()
    {
        var fx = new TestFixture();
        var heic = Row("abc123", "2024/live.heic", pairedAssetId: MovId);
        var mov = Row("cafebabe", "2023/live.mov", id: MovId);
        fx.Store(heic, mov);

        var result = await fx.ClassifyAsync(Pair("2024/live.heic", "2024/live.mov"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.Unchanged, heic));
        result.Secondary.Should().Be(new FileClassification(FileOutcome.Moved, mov));
    }

    [Fact]
    public async Task ClassifyAsync_NewHeicWithStoredMov_KeepsTheMovRowForPersistence()
    {
        var fx = new TestFixture();
        var mov = Row("cafebabe", "live.mov");
        fx.Store(mov);

        var result = await fx.ClassifyAsync(Pair());

        result.NeedsProcessing.Should().BeTrue();
        result.Secondary.Should().Be(new FileClassification(FileOutcome.Unchanged, mov));
        result.Pairing.Should().Be(PairingAction.None);
    }

    [Fact]
    public async Task ClassifyAsync_StoredHeicWithoutAPairedMov_AttachesTheMov()
    {
        var fx = new TestFixture();
        fx.Store(Row("abc123", "live.heic"));

        (await fx.ClassifyAsync(Pair())).Pairing.Should().Be(PairingAction.AttachMov);
    }

    [Fact]
    public async Task ClassifyAsync_HeicPairedToThisStoredMov_BackfillsTheMovsProxies()
    {
        var fx = new TestFixture();
        fx.Store(Row("abc123", "live.heic", pairedAssetId: MovId), Row("cafebabe", "live.mov", id: MovId));

        (await fx.ClassifyAsync(Pair())).Pairing.Should().Be(PairingAction.BackfillMovProxy);
    }

    [Fact]
    public async Task ClassifyAsync_HeicPairedToADifferentMov_LeavesThePairingAlone()
    {
        var fx = new TestFixture();
        fx.Store(Row("abc123", "live.heic", pairedAssetId: MovId), Row("cafebabe", "live.mov"));

        (await fx.ClassifyAsync(Pair())).Pairing.Should().Be(PairingAction.None);
    }

    [Fact]
    public async Task ClassifyAsync_HeicPairedButThisMovNotStored_LeavesThePairingAlone()
    {
        var fx = new TestFixture();
        fx.Store(Row("abc123", "live.heic", pairedAssetId: MovId));

        (await fx.ClassifyAsync(Pair())).Pairing.Should().Be(PairingAction.None);
    }

    [Fact]
    public async Task ClassifyAsync_EditedHeicPairedToThisStoredMov_LeavesThePairingToPersistence()
    {
        var fx = new TestFixture();
        var heic = Row("f00d", "live.heic", pairedAssetId: MovId);
        fx.Store(heic, Row("cafebabe", "live.mov", id: MovId));

        var result = await fx.ClassifyAsync(Pair());

        result.Primary.Should().Be(new FileClassification(FileOutcome.ContentChanged, heic));
        result.Pairing.Should().Be(PairingAction.None);
    }

    [Fact]
    public async Task ClassifyAsync_EditedUnpairedHeic_LeavesThePairingToPersistence()
    {
        var fx = new TestFixture();
        fx.Store(Row("f00d", "live.heic"));

        (await fx.ClassifyAsync(Pair())).Pairing.Should().Be(PairingAction.None);
    }

    [Fact]
    public async Task ClassifyAsync_EditedMov_IsContentChangedOnItsOwn()
    {
        var fx = new TestFixture();
        var heic = Row("abc123", "live.heic", pairedAssetId: MovId);
        var mov = Row("beef", "live.mov", id: MovId);
        fx.Store(heic, mov);

        var result = await fx.ClassifyAsync(Pair());

        result.Primary.Should().Be(new FileClassification(FileOutcome.Unchanged, heic));
        result.Secondary.Should().Be(new FileClassification(FileOutcome.ContentChanged, mov));
    }

    [Fact]
    public async Task ClassifyAsync_MovCopiedOverThePathOfAnActiveRow_StaysADuplicateCopy()
    {
        var fx = new TestFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/live.mov");
        var storedMov = Row("cafebabe", "2023/live.mov");
        fx.Store(storedMov, Row("beef", "copy/live.mov"));

        var result = await fx.ClassifyAsync(Pair("copy/live.heic", "copy/live.mov"));

        result.Secondary.Should().Be(new FileClassification(FileOutcome.DuplicateCopy, storedMov));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassifyAsync_MovMovedOntoThePathOfAnotherRow_StaysPathTaken(bool holderSoftDeleted)
    {
        var fx = new TestFixture();
        var storedMov = Row("cafebabe", "2023/live.mov");
        fx.Store(storedMov, Row("beef", "2024/live.mov", softDeleted: holderSoftDeleted));

        var result = await fx.ClassifyAsync(Pair("2024/live.heic", "2024/live.mov"));

        result.Secondary.Should().Be(new FileClassification(FileOutcome.PathTaken, storedMov));
    }

    [Fact]
    public async Task ClassifyAsync_HeicMovedOntoAnotherLivePhoto_IsOverwrittenByMoveAndLeavesThePairingAlone()
    {
        var fx = new TestFixture();
        var (heic, heicAtPath) = (Row("abc123", "2023/live.heic"), Row("f00d", "2024/live.heic", pairedAssetId: MovId));
        var (mov, movAtPath) = (Row("cafebabe", "2023/live.mov"), Row("beef", "2024/live.mov", id: MovId));
        fx.Store(heic, heicAtPath, mov, movAtPath);

        var result = await fx.ClassifyAsync(Pair("2024/live.heic", "2024/live.mov"));

        result.Primary.Should().Be(new FileClassification(FileOutcome.OverwrittenByMove, heicAtPath, Retired: heic));
        result.Secondary.Should().Be(new FileClassification(FileOutcome.PathTaken, mov));
        result.Pairing.Should().Be(PairingAction.None);
    }

    [Fact]
    public async Task ClassifyAsync_HeicCopiedOverAnotherRowsPath_PairsAsForADuplicateCopy()
    {
        var fx = new TestFixture();
        fx.FileSystem.AddEmptyFile("/abs/2023/live.heic");
        fx.Store(Row("abc123", "2023/live.heic"), Row("f00d", "copy/live.heic"));

        var result = await fx.ClassifyAsync(Pair("copy/live.heic", "copy/live.mov"));

        result.Primary.Outcome.Should().Be(FileOutcome.OverwrittenByCopy);
        result.Pairing.Should().Be(PairingAction.AttachMov);
    }

    [Fact]
    public async Task ClassifyAsync_MovWithOtherContentAtItsSoftDeletedPath_IsAutoRestoredContentChangedOnItsOwn()
    {
        var fx = new TestFixture();
        var heic = Row("abc123", "live.heic", pairedAssetId: MovId);
        var mov = Row("beef", "live.mov", softDeleted: true, id: MovId);
        fx.Store(heic, mov);

        var result = await fx.ClassifyAsync(Pair());

        result.Secondary.Should().Be(new FileClassification(FileOutcome.AutoRestoredContentChanged, mov));
        result.NeedsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_AutoRestoredHeicPairedToThisStoredMov_BackfillsTheMovsProxies()
    {
        var fx = new TestFixture();
        fx.Store(
            Row("abc123", "live.heic", softDeleted: true, pairedAssetId: MovId),
            Row("cafebabe", "live.mov", softDeleted: true, id: MovId));

        var result = await fx.ClassifyAsync(Pair());

        result.Primary.Outcome.Should().Be(FileOutcome.AutoRestored);
        result.Secondary!.Outcome.Should().Be(FileOutcome.AutoRestored);
        result.Pairing.Should().Be(PairingAction.BackfillMovProxy);
    }

    [Fact]
    public async Task ClassifyAsync_HeicAutoRestoredWithOtherContent_LeavesThePairingToPersistence()
    {
        var fx = new TestFixture();
        fx.Store(Row("f00d", "live.heic", softDeleted: true));

        (await fx.ClassifyAsync(Pair())).Pairing.Should().Be(PairingAction.None);
    }
}
