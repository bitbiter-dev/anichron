using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class AssetInteractionWriterTests
{
    private sealed class Library
    {
        public Library()
        {
            MiraConfig = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Mira.Id, RootPath = "/data/originals/mira", IsActive = true };
            Photo = new MediaAsset
            {
                Id = Guid.NewGuid(),
                StorageConfigId = MiraConfig.Id,
                FilePath = "2019/beach.jpg",
                FileName = "beach.jpg",
                ContentHash = "0123456789abcdef",
                DateCaptured = new LocalDateTime(2019, 7, 14, 9, 30),
                MediaType = MediaType.Image,
            };
        }

        public DbContextOptions<AnichronDbContext> Options { get; } = AnichronDbContexts.InMemoryOptions();
        public User Mira { get; } = new() { Id = Guid.NewGuid(), Username = "mira", Email = "mira@example.com" };
        public User Alice { get; } = new() { Id = Guid.NewGuid(), Username = "alice", Email = "alice@example.com" };
        public UserStorageConfig MiraConfig { get; }
        public MediaAsset Photo { get; }

        public async Task SeedAsync(params AssetInteraction[] interactions)
        {
            await using var db = new AnichronDbContext(Options);
            db.Users.AddRange(Mira, Alice);
            db.StorageConfigs.Add(MiraConfig);
            db.MediaAssets.Add(Photo);
            db.Interactions.AddRange(interactions);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        public async Task<InteractionState?> ApplyAsync(User caller, InteractionChange change, IInterceptor? interceptor = null)
        {
            var options = interceptor is null
                ? Options
                : new DbContextOptionsBuilder<AnichronDbContext>(Options).AddInterceptors(interceptor).Options;
            await using var db = new AnichronDbContext(options);
            return await new EfAssetInteractionWriter(db)
                .ApplyAsync(caller.Id, Photo.Id, change, TestContext.Current.CancellationToken);
        }

        public async Task<IReadOnlyList<AssetView>> OnThisDayAsync(User caller)
        {
            await using var db = new AnichronDbContext(Options);
            return await new EfAssetViewReader(db)
                .ReadAsync(caller.Id, new OnThisDay(new LocalDate(2026, 7, 14)), TestContext.Current.CancellationToken);
        }

        public async Task<List<AssetInteraction>> StoredAsync()
        {
            await using var db = new AnichronDbContext(Options);
            return await db.Interactions.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(true, null, null)]
    [InlineData(null, true, null)]
    [InlineData(null, null, true)]
    public async Task ApplyAsync_FirstWrite_CreatesTheCallersRowAndReturnsItsState(bool? starred, bool? liked, bool? hidden)
    {
        var library = new Library();
        await library.SeedAsync();

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(starred, liked, hidden));

        var expected = new InteractionState(starred ?? false, liked ?? false, hidden ?? false);
        state.Should().Be(expected);
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        row.UserId.Should().Be(library.Mira.Id);
        row.AssetId.Should().Be(library.Photo.Id);
        new InteractionState(row.IsStarred, row.IsLiked, row.IsHidden).Should().Be(expected);
    }

    private sealed class ConcurrentFirstWrite(Library library, string indexName, int losses = 1) : SaveChangesInterceptor
    {
        public int Saves { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves++;
            if (Saves > losses)
                return result;

            await using var other = new AnichronDbContext(library.Options);
            other.Interactions.Add(MirasRow(library, liked: true));
            await other.SaveChangesAsync(cancellationToken);
            throw DatabaseErrors.UniqueViolation(indexName);
        }
    }

    private static AssetInteraction MirasRow(Library library, bool starred = false, bool liked = false, bool hidden = false)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = library.Mira.Id,
            AssetId = library.Photo.Id,
            IsStarred = starred,
            IsLiked = liked,
            IsHidden = hidden,
        };

    [Fact]
    public async Task ApplyAsync_PartialChange_KeepsTheOtherFlags()
    {
        var library = new Library();
        await library.SeedAsync(MirasRow(library, starred: true, hidden: true));

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: true, Hidden: null));

        state.Should().Be(new InteractionState(Starred: true, Liked: true, Hidden: true));
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        (row.IsStarred, row.IsLiked, row.IsHidden).Should().Be((true, true, true));
    }

    [Fact]
    public async Task ApplyAsync_FalseFlag_ClearsOnlyThatFlag()
    {
        var library = new Library();
        await library.SeedAsync(MirasRow(library, starred: true, liked: true));

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: false, Liked: null, Hidden: null));

        state.Should().Be(new InteractionState(Starred: false, Liked: true, Hidden: false));
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        (row.IsStarred, row.IsLiked, row.IsHidden).Should().Be((false, true, false));
    }

    [Fact]
    public async Task ApplyAsync_HiddenAsset_CanBeUnhidden()
    {
        var library = new Library();
        await library.SeedAsync(MirasRow(library, hidden: true));

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: false));

        state.Should().Be(new InteractionState(Starred: false, Liked: false, Hidden: false));
        (await library.StoredAsync()).Should().ContainSingle().Which.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_EmptyChangeWithoutARow_ReturnsTheDefaultStateAndStoresNothing()
    {
        var library = new Library();
        await library.SeedAsync();

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: null));

        state.Should().Be(new InteractionState(Starred: false, Liked: false, Hidden: false));
        (await library.StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_EmptyChangeWithARow_ReturnsTheStoredState()
    {
        var library = new Library();
        await library.SeedAsync(MirasRow(library, liked: true));

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: null));

        state.Should().Be(new InteractionState(Starred: false, Liked: true, Hidden: false));
    }

    [Fact]
    public async Task ApplyAsync_AnotherUsersAsset_ReturnsNullAndLeavesTheOwnersRowAlone()
    {
        var library = new Library();
        await library.SeedAsync(MirasRow(library));

        var state = await library.ApplyAsync(library.Alice, new InteractionChange(Starred: true, Liked: true, Hidden: true));

        state.Should().BeNull();
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        row.UserId.Should().Be(library.Mira.Id);
        (row.IsStarred, row.IsLiked, row.IsHidden).Should().Be((false, false, false));
    }

    [Fact]
    public async Task ApplyAsync_UnknownAsset_ReturnsNullAndStoresNothing()
    {
        var library = new Library();
        await library.SeedAsync();
        await using var db = new AnichronDbContext(library.Options);

        var state = await new EfAssetInteractionWriter(db).ApplyAsync(
            library.Mira.Id, Guid.NewGuid(), new InteractionChange(Starred: true, Liked: null, Hidden: null),
            TestContext.Current.CancellationToken);

        state.Should().BeNull();
        (await library.StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_SoftDeletedAsset_ReturnsNull()
    {
        var library = new Library();
        library.Photo.IsSoftDeleted = true;
        await library.SeedAsync();

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null));

        state.Should().BeNull();
        (await library.StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_AssetOfAnInactiveConfig_ReturnsNull()
    {
        var library = new Library();
        library.MiraConfig.IsActive = false;
        await library.SeedAsync();

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null));

        state.Should().BeNull();
        (await library.StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_LosingTheFirstWriteRace_AppliesTheChangeToTheWinnersRow()
    {
        var library = new Library();
        await library.SeedAsync();
        var race = new ConcurrentFirstWrite(library, InteractionIndexNames.OnePerUserAndAsset);

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null), race);

        state.Should().Be(new InteractionState(Starred: true, Liked: true, Hidden: false));
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        (row.IsStarred, row.IsLiked, row.IsHidden).Should().Be((true, true, false));
        race.Saves.Should().Be(2);
    }

    [Fact]
    public async Task ApplyAsync_LosingTwice_Throws()
    {
        var library = new Library();
        await library.SeedAsync();
        var race = new ConcurrentFirstWrite(library, InteractionIndexNames.OnePerUserAndAsset, losses: 2);

        var apply = () => library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null), race);

        await apply.Should().ThrowAsync<DbUpdateException>();
        race.Saves.Should().Be(2);
    }

    [Fact]
    public async Task ApplyAsync_UniqueViolationOfAnotherIndex_IsNotRetried()
    {
        var library = new Library();
        await library.SeedAsync();
        var race = new ConcurrentFirstWrite(library, "ix_some_other_index");

        var apply = () => library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null), race);

        await apply.Should().ThrowAsync<DbUpdateException>();
        race.Saves.Should().Be(1);
    }

    // ==========================================================================
    // First caller: what the caller's On This Day read sees after a write
    // ==========================================================================

    [Fact]
    public async Task ApplyAsync_StarAndLike_AreReadBackOnThisDay()
    {
        var library = new Library();
        await library.SeedAsync();

        await library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: true, Hidden: null));

        var view = (await library.OnThisDayAsync(library.Mira)).Should().ContainSingle().Which;
        view.Id.Should().Be(library.Photo.Id);
        (view.IsStarred, view.IsLiked).Should().Be((true, true));
    }

    [Fact]
    public async Task ApplyAsync_Hide_DropsTheAssetFromOnThisDayAndUnhideBringsItBack()
    {
        var library = new Library();
        await library.SeedAsync();

        await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: true));
        (await library.OnThisDayAsync(library.Mira)).Should().BeEmpty();

        await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: false));
        (await library.OnThisDayAsync(library.Mira)).Should().ContainSingle().Which.Id.Should().Be(library.Photo.Id);
    }
}
