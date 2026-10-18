using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Ingestion;
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

    // ==========================================================================
    // TrySaveNewAsync
    // ==========================================================================

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
}
