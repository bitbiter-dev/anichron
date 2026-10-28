using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.EntityFrameworkCore;
using NSubstitute.ExceptionExtensions;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class IngestionSaverTests
{
    private static readonly Guid MovId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ConfigId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class TestFixture
    {
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IngestionContext Context { get; } = new()
        {
            Item = new SingleFileItem("/abs/photo.heic", "photo.heic", MediaType.Image),
            Config = new UserStorageConfig { Id = ConfigId, UserId = Guid.NewGuid(), RootPath = "/abs" },
            AttemptId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            Classification = new(new FileClassification(FileOutcome.New, null), null, PairingAction.None),
        };

        public ProxyFile OwnProxy { get; }

        public TestFixture()
        {
            OwnProxy = new ProxyFile { AssetId = Context.AssetId, ProxyType = ProxyType.Thumbnail };
            Context.AddProxyFile(OwnProxy);
            Context.AddProxyFile(new ProxyFile { AssetId = MovId, ProxyType = ProxyType.WebVideo });
        }

        public void SaveFailsWith(Exception ex) => UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(ex);

        public IngestionSaver Build() => new(UnitOfWork, Repository);

        public void TransactionsRunTheirWork()
            => UnitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task>>()());
    }

    private static readonly Guid RetiredId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid RetiredVideoId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static void RetireFirst(IngestionContext context)
        => context.Classification = new(
            new FileClassification(FileOutcome.OverwrittenByMove, new MediaAsset(), Retired: new MediaAsset { Id = RetiredId }),
            new FileClassification(FileOutcome.OverwrittenByMove, new MediaAsset(), Retired: new MediaAsset { Id = RetiredVideoId }),
            PairingAction.None);

    private static List<string> RecordSteps(TestFixture fx)
    {
        var steps = new List<string>();
        fx.UnitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                steps.Add("begin");
                await call.Arg<Func<Task>>()();
                steps.Add("commit");
            });
        fx.Repository.SoftDeleteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                steps.Add($"soft-delete {string.Join(',', call.Arg<IReadOnlyCollection<Guid>>())}");
                return 1;
            });
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            steps.Add("save");
            return 1;
        });
        return steps;
    }

    private static MediaAsset NewAsset => new() { ContentHash = "deadbeef", StorageConfigId = ConfigId };

    // ==========================================================================
    // SaveAsync
    // ==========================================================================

    [Fact]
    public async Task SaveAsync_Commits_AndHandsTheMovsProxiesToTheDatabase()
    {
        var fx = new TestFixture();

        await fx.Build().SaveAsync(fx.Context, MovId, CancellationToken.None);

        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        fx.Context.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(fx.OwnProxy);
    }

    [Fact]
    public async Task SaveAsync_WithoutAMov_KeepsEveryProxy()
    {
        var fx = new TestFixture();

        await fx.Build().SaveAsync(fx.Context, null, CancellationToken.None);

        fx.Context.ProxyFiles.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(MediaAssetIndexNames.ActiveContentHashUnique)]
    [InlineData(ProxyFileIndexNames.OnePerAssetAndType)]
    public async Task SaveAsync_LosesARace_ReleasesTheMovsProxiesBeforeRethrowing(string indexName)
    {
        var fx = new TestFixture();
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(indexName));

        var act = () => fx.Build().SaveAsync(fx.Context, MovId, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        fx.Context.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(fx.OwnProxy);
    }

    [Fact]
    public async Task SaveAsync_FailsForAnotherReason_KeepsTheProxiesForCompensation()
    {
        var fx = new TestFixture();
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.FilePathUnique));

        var act = () => fx.Build().SaveAsync(fx.Context, MovId, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        fx.Context.ProxyFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAsync_Commits_CountsTheStagedChanges()
    {
        var fx = new TestFixture();
        fx.Context.StageSoftDeleted();
        fx.Context.StageAutoRestored();
        fx.Context.StageAutoRestored();

        await fx.Build().SaveAsync(fx.Context, null, CancellationToken.None);

        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((1, 2));
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((0, 0));
    }

    [Fact]
    public async Task SaveAsync_SecondSaveWithNothingStaged_CountsNothingMore()
    {
        var fx = new TestFixture();
        var saver = fx.Build();
        fx.Context.StageSoftDeleted();
        fx.Context.StageAutoRestored();
        await saver.SaveAsync(fx.Context, null, CancellationToken.None);

        await saver.SaveAsync(fx.Context, null, CancellationToken.None);

        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((1, 1));
    }

    [Fact]
    public async Task SaveAsync_LosesARace_CountsNothing()
    {
        var fx = new TestFixture();
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Context.StageSoftDeleted();
        fx.Context.StageAutoRestored();

        var act = () => fx.Build().SaveAsync(fx.Context, null, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((0, 0));
    }

    // ==========================================================================
    // TrySaveNewAsync
    // ==========================================================================

    [Fact]
    public async Task TrySaveNewAsync_LosesTheRaceToAConsumerThatCommittedTheContent_CountsNothing()
    {
        var fx = new TestFixture();
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Repository.FindByHashAsync("deadbeef", ConfigId, Arg.Any<CancellationToken>())
            .Returns(new MediaAsset { ContentHash = "deadbeef" });
        fx.Context.StageAutoRestored();

        await fx.Build().TrySaveNewAsync(fx.Context, NewAsset, null, CancellationToken.None);

        fx.Context.AutoRestored.Should().Be(0);
    }

    [Fact]
    public async Task TrySaveNewAsync_Commits_ReturnsTrue()
    {
        var fx = new TestFixture();

        var saved = await fx.Build().TrySaveNewAsync(fx.Context, NewAsset, MovId, CancellationToken.None);

        saved.Should().BeTrue();
        fx.Context.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(fx.OwnProxy);
    }

    [Fact]
    public async Task TrySaveNewAsync_LosesTheRaceToAConsumerThatCommittedTheContent_ReturnsFalse()
    {
        var fx = new TestFixture();
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Repository.FindByHashAsync("deadbeef", ConfigId, Arg.Any<CancellationToken>())
            .Returns(new MediaAsset { ContentHash = "deadbeef" });

        var saved = await fx.Build().TrySaveNewAsync(fx.Context, NewAsset, null, CancellationToken.None);

        saved.Should().BeFalse();
    }

    [Fact]
    public async Task TrySaveNewAsync_LosesARaceButTheContentIsNotCommitted_ReleasesTheMovsProxiesAndRethrows()
    {
        var fx = new TestFixture();
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));

        var act = () => fx.Build().TrySaveNewAsync(fx.Context, NewAsset, MovId, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        fx.Context.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(fx.OwnProxy);
    }

    [Fact]
    public async Task TrySaveNewAsync_FailsForAnotherReason_RethrowsWithoutLookup()
    {
        var fx = new TestFixture();
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.FilePathUnique));

        var act = () => fx.Build().TrySaveNewAsync(fx.Context, NewAsset, MovId, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        await fx.Repository.DidNotReceive()
            .FindByHashAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        fx.Context.ProxyFiles.Should().HaveCount(2);
    }

    // ==========================================================================
    // TrySaveRetiringFirstAsync
    // ==========================================================================

    [Fact]
    public async Task TrySaveRetiringFirstAsync_SoftDeletesEveryRetiredRowBeforeSavingInOneTransaction()
    {
        var fx = new TestFixture();
        RetireFirst(fx.Context);
        var steps = RecordSteps(fx);

        var saved = await fx.Build().TrySaveRetiringFirstAsync(fx.Context, CancellationToken.None);

        saved.Should().BeTrue();
        steps.Should().Equal(
            "begin", "soft-delete dddddddd-dddd-dddd-dddd-dddddddddddd,eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", "save", "commit");
    }
    [Fact]
    public async Task TrySaveRetiringFirstAsync_Commits_CountsTheStagedChanges()
    {
        var fx = new TestFixture();
        fx.TransactionsRunTheirWork();
        RetireFirst(fx.Context);
        fx.Context.StageSoftDeleted();
        fx.Context.StageAutoRestored();

        await fx.Build().TrySaveRetiringFirstAsync(fx.Context, CancellationToken.None);

        (fx.Context.SoftDeleted, fx.Context.AutoRestored).Should().Be((1, 1));
        (fx.Context.StagedSoftDeleted, fx.Context.StagedAutoRestored).Should().Be((0, 0));
    }

    [Fact]
    public async Task TrySaveRetiringFirstAsync_LostTheRaceForTheContent_CountsNothing()
    {
        var fx = new TestFixture();
        fx.TransactionsRunTheirWork();
        RetireFirst(fx.Context);
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Context.StageSoftDeleted();

        await fx.Build().TrySaveRetiringFirstAsync(fx.Context, CancellationToken.None);

        fx.Context.SoftDeleted.Should().Be(0);
    }

    [Fact]
    public async Task TrySaveRetiringFirstAsync_LostTheRaceForTheContent_SkipsWithoutThrowing()
    {
        var fx = new TestFixture();
        fx.TransactionsRunTheirWork();
        RetireFirst(fx.Context);
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));

        var saved = await fx.Build().TrySaveRetiringFirstAsync(fx.Context, CancellationToken.None);

        saved.Should().BeFalse();
    }

    [Fact]
    public async Task TrySaveRetiringFirstAsync_SaveFailsForAnotherReason_Rethrows()
    {
        var fx = new TestFixture();
        fx.TransactionsRunTheirWork();
        RetireFirst(fx.Context);
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.FilePathUnique));

        var act = () => fx.Build().TrySaveRetiringFirstAsync(fx.Context, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TrySaveNewAsync_NothingRetiredFirst_SavesWithoutATransaction()
    {
        var fx = new TestFixture();

        await fx.Build().TrySaveNewAsync(fx.Context, NewAsset, MovId, CancellationToken.None);

        await fx.UnitOfWork.DidNotReceive().ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySaveNewAsync_RowsRetiredFirst_SoftDeletesThemBeforeSavingInOneTransaction()
    {
        var fx = new TestFixture();
        RetireFirst(fx.Context);
        var steps = RecordSteps(fx);

        var saved = await fx.Build().TrySaveNewAsync(fx.Context, NewAsset, MovId, CancellationToken.None);

        saved.Should().BeTrue();
        steps.Should().Equal(
            "begin", "soft-delete dddddddd-dddd-dddd-dddd-dddddddddddd,eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", "save", "commit");
        fx.Context.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(fx.OwnProxy);
    }

    [Fact]
    public async Task TrySaveNewAsync_RowsRetiredFirstAndARaceLost_ReleasesTheMovsProxies()
    {
        var fx = new TestFixture();
        RetireFirst(fx.Context);
        fx.TransactionsRunTheirWork();
        fx.SaveFailsWith(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Repository.FindByHashAsync("deadbeef", ConfigId, Arg.Any<CancellationToken>()).Returns(new MediaAsset());

        var saved = await fx.Build().TrySaveNewAsync(fx.Context, NewAsset, MovId, CancellationToken.None);

        saved.Should().BeFalse();
        fx.Context.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(fx.OwnProxy);
    }

    [Fact]
    public async Task SaveAsync_RowsRetiredFirst_SavesWithoutRetiringThem()
    {
        var fx = new TestFixture();
        RetireFirst(fx.Context);

        await fx.Build().SaveAsync(fx.Context, MovId, CancellationToken.None);

        await fx.Repository.DidNotReceive().SoftDeleteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
