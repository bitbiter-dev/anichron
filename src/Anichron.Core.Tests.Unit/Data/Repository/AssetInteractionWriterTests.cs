using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Testing.Domain;
using Anichron.Testing.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class AssetInteractionWriterTests
{
    private sealed class Library : IAsyncDisposable
    {
        private readonly Timeline timeline = new();

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
                Captured = Recorded.At(new LocalDateTime(2019, 7, 14, 9, 30)),
                MediaType = MediaType.Image,
            };
        }

        public DbContextOptions<AnichronDbContext> Options { get; } = AnichronDbContexts.InMemoryOptions();
        public User Mira { get; } = new() { Id = Guid.NewGuid(), Username = "mira", Email = "mira@example.com" };
        public User Alice { get; } = new() { Id = Guid.NewGuid(), Username = "alice", Email = "alice@example.com" };
        public UserStorageConfig MiraConfig { get; }
        public MediaAsset Photo { get; }
        public FakeTimeProvider Clock => timeline.Clock;

        public ValueTask DisposeAsync() => timeline.DisposeAsync();

        public async Task SeedAsync(params AssetInteraction[] interactions)
        {
            await using var db = new AnichronDbContext(Options);
            db.Users.AddRange(Mira, Alice);
            db.StorageConfigs.Add(MiraConfig);
            db.MediaAssets.Add(Photo);
            db.Interactions.AddRange(interactions);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        public async Task<InteractionState?> ApplyAsync(
            User caller, InteractionChange change, IInterceptor? interceptor = null, Guid? assetId = null)
        {
            var options = interceptor is null
                ? Options
                : new DbContextOptionsBuilder<AnichronDbContext>(Options).AddInterceptors(interceptor).Options;
            await using var db = new AnichronDbContext(options);
            return await new EfAssetInteractionWriter(db, Clock)
                .ApplyAsync(caller.Id, assetId ?? Photo.Id, change, TestContext.Current.CancellationToken);
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
        await using var library = new Library();
        await library.SeedAsync();

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(starred, liked, hidden));

        var expected = new InteractionState(starred ?? false, liked ?? false, hidden ?? false);
        state.Should().Be(expected);
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        row.UserId.Should().Be(library.Mira.Id);
        row.AssetId.Should().Be(library.Photo.Id);
        new InteractionState(row.IsStarred, row.IsLiked, row.HiddenAt is not null).Should().Be(expected);
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
            HiddenAt = hidden ? Instant.FromUtc(2026, 11, 1, 12, 0) : null,
        };

    [Fact]
    public async Task ApplyAsync_PartialChange_KeepsTheOtherFlags()
    {
        await using var library = new Library();
        await library.SeedAsync(MirasRow(library, starred: true, hidden: true));

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: true, Hidden: null));

        state.Should().Be(new InteractionState(Starred: true, Liked: true, Hidden: true));
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        (row.IsStarred, row.IsLiked, row.HiddenAt is not null).Should().Be((true, true, true));
    }

    [Fact]
    public async Task ApplyAsync_FalseFlag_ClearsOnlyThatFlag()
    {
        await using var library = new Library();
        await library.SeedAsync(MirasRow(library, starred: true, liked: true));

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: false, Liked: null, Hidden: null));

        state.Should().Be(new InteractionState(Starred: false, Liked: true, Hidden: false));
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        (row.IsStarred, row.IsLiked, row.HiddenAt is not null).Should().Be((false, true, false));
    }

    [Fact]
    public async Task ApplyAsync_HiddenAsset_CanBeUnhidden()
    {
        await using var library = new Library();
        await library.SeedAsync(MirasRow(library, hidden: true));

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: false));

        state.Should().Be(new InteractionState(Starred: false, Liked: false, Hidden: false));
        (await library.StoredAsync()).Should().ContainSingle().Which.HiddenAt.Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_EmptyChangeWithoutARow_ReturnsTheDefaultStateAndStoresNothing()
    {
        await using var library = new Library();
        await library.SeedAsync();

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: null));

        state.Should().Be(new InteractionState(Starred: false, Liked: false, Hidden: false));
        (await library.StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_EmptyChangeWithARow_ReturnsTheStoredState()
    {
        await using var library = new Library();
        await library.SeedAsync(MirasRow(library, liked: true));

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: null));

        state.Should().Be(new InteractionState(Starred: false, Liked: true, Hidden: false));
    }

    [Fact]
    public async Task ApplyAsync_AnotherUsersAsset_ReturnsNullAndLeavesTheOwnersRowAlone()
    {
        await using var library = new Library();
        await library.SeedAsync(MirasRow(library));

        var state = await library.ApplyAsync(library.Alice, new InteractionChange(Starred: true, Liked: true, Hidden: true));

        state.Should().BeNull();
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        row.UserId.Should().Be(library.Mira.Id);
        (row.IsStarred, row.IsLiked, row.HiddenAt is not null).Should().Be((false, false, false));
    }

    [Fact]
    public async Task ApplyAsync_UnknownAsset_ReturnsNullAndStoresNothing()
    {
        await using var library = new Library();
        await library.SeedAsync();

        var state = await library.ApplyAsync(
            library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null), assetId: Guid.NewGuid());

        state.Should().BeNull();
        (await library.StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public void Query_FiltersVisibilityAndTheInteractionOnTheCaller()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetInteractionWriter.Query(db, Guid.NewGuid(), Guid.NewGuid()).ToQueryString();

        sql.Should().Contain("JOIN \"StorageConfigs\"")
            .And.Contain("\"IsActive\"")
            .And.Contain("\"IsSoftDeleted\"")
            .And.Contain("FROM \"Interactions\"")
            .And.NotContain("EXISTS", "a hidden asset must stay writable, so it can be unhidden");
        sql.Split("\"UserId\" = @").Should().HaveCount(3,
            "the owned-config check and the interaction lookup each filter on the caller");
    }

    [Fact]
    public async Task ApplyAsync_SoftDeletedAsset_ReturnsNull()
    {
        await using var library = new Library();
        library.Photo.IsSoftDeleted = true;
        await library.SeedAsync();

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null));

        state.Should().BeNull();
        (await library.StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_AssetOfAnInactiveConfig_ReturnsNull()
    {
        await using var library = new Library();
        library.MiraConfig.IsActive = false;
        await library.SeedAsync();

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null));

        state.Should().BeNull();
        (await library.StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_LosingTheFirstWriteRace_AppliesTheChangeToTheWinnersRow()
    {
        await using var library = new Library();
        await library.SeedAsync();
        var race = new ConcurrentFirstWrite(library, InteractionIndexNames.OnePerUserAndAsset);

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null), race);

        state.Should().Be(new InteractionState(Starred: true, Liked: true, Hidden: false));
        var row = (await library.StoredAsync()).Should().ContainSingle().Which;
        (row.IsStarred, row.IsLiked, row.HiddenAt is not null).Should().Be((true, true, false));
        race.Saves.Should().Be(2);
    }

    [Fact]
    public async Task ApplyAsync_LosingTwice_Throws()
    {
        await using var library = new Library();
        await library.SeedAsync();
        var race = new ConcurrentFirstWrite(library, InteractionIndexNames.OnePerUserAndAsset, losses: 2);

        var apply = () => library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null), race);

        await apply.Should().ThrowAsync<DbUpdateException>();
        race.Saves.Should().Be(2);
    }

    [Fact]
    public async Task ApplyAsync_UniqueViolationOfAnotherIndex_IsNotRetried()
    {
        await using var library = new Library();
        await library.SeedAsync();
        var race = new ConcurrentFirstWrite(library, "ix_some_other_index");

        var apply = () => library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null), race);

        await apply.Should().ThrowAsync<DbUpdateException>();
        race.Saves.Should().Be(1);
    }

    // ==========================================================================
    // When it was hidden (#558)
    // ==========================================================================

    [Fact]
    public async Task ApplyAsync_Hide_StampsTheCurrentTime()
    {
        await using var library = new Library();
        await library.SeedAsync();
        library.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 20, 8, 15, 0, TimeSpan.Zero));

        await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: true));

        (await library.StoredAsync()).Should().ContainSingle().Which.HiddenAt.Should().Be(Instant.FromUtc(2026, 11, 20, 8, 15));
    }

    [Fact]
    public async Task ApplyAsync_HideAgain_RestampsAHiddenAsset()
    {
        await using var library = new Library();
        await library.SeedAsync(MirasRow(library, hidden: true));
        library.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 21, 7, 0, 0, TimeSpan.Zero));

        var state = await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: true));

        state.Should().Be(new InteractionState(Starred: false, Liked: false, Hidden: true));
        (await library.StoredAsync()).Should().ContainSingle().Which.HiddenAt.Should().Be(Instant.FromUtc(2026, 11, 21, 7, 0));
    }

    [Fact]
    public async Task ApplyAsync_HideUnhideHide_ClearsThenStampsTheLaterTime()
    {
        await using var library = new Library();
        await library.SeedAsync();
        var hide = new InteractionChange(Starred: null, Liked: null, Hidden: true);
        library.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero));
        await library.ApplyAsync(library.Mira, hide);

        await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: false));
        (await library.StoredAsync()).Should().ContainSingle().Which.HiddenAt.Should().BeNull();

        library.Clock.Advance(TimeSpan.FromHours(3));
        await library.ApplyAsync(library.Mira, hide);
        (await library.StoredAsync()).Should().ContainSingle().Which.HiddenAt.Should().Be(Instant.FromUtc(2026, 11, 20, 11, 0));
    }

    [Fact]
    public async Task ApplyAsync_ChangeWithoutHidden_KeepsWhenItWasHidden()
    {
        await using var library = new Library();
        await library.SeedAsync(MirasRow(library, hidden: true));
        library.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 21, 7, 0, 0, TimeSpan.Zero));

        await library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: null, Hidden: null));

        (await library.StoredAsync()).Should().ContainSingle().Which.HiddenAt.Should().Be(Instant.FromUtc(2026, 11, 1, 12, 0));
    }

    // ==========================================================================
    // Read back On This Day
    // ==========================================================================

    [Fact]
    public async Task ApplyAsync_StarAndLike_AreReadBackOnThisDay()
    {
        await using var library = new Library();
        await library.SeedAsync();

        await library.ApplyAsync(library.Mira, new InteractionChange(Starred: true, Liked: true, Hidden: null));

        var view = (await library.OnThisDayAsync(library.Mira)).Should().ContainSingle().Which;
        view.Id.Should().Be(library.Photo.Id);
        (view.IsStarred, view.IsLiked).Should().Be((true, true));
    }

    [Fact]
    public async Task ApplyAsync_Hide_DropsTheAssetFromOnThisDayAndUnhideBringsItBack()
    {
        await using var library = new Library();
        await library.SeedAsync();

        await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: true));
        (await library.OnThisDayAsync(library.Mira)).Should().BeEmpty();

        await library.ApplyAsync(library.Mira, new InteractionChange(Starred: null, Liked: null, Hidden: false));
        (await library.OnThisDayAsync(library.Mira)).Should().ContainSingle().Which.Id.Should().Be(library.Photo.Id);
    }
}
