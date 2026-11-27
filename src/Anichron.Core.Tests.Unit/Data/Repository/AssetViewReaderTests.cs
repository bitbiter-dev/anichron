using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Text.RegularExpressions;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed partial class AssetViewReaderTests
{
    private sealed class Everything(AssetSet set = AssetSet.Shown) : IAssetSelection
    {
        public AssetSet Set => set;

        public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets) => assets;
    }

    private sealed class OnlyAsset(Guid id) : IAssetSelection
    {
        public AssetSet Set => AssetSet.Shown;

        public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets) => assets.Where(a => a.Id == id);
    }

    private static async Task<IReadOnlyList<AssetView>> ReadAsync(AssetLibrary library, User caller, IAssetSelection? selection = null)
    {
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);
        return await new EfAssetViewReader(db)
            .ReadAsync(caller.Id, selection ?? new Everything(), TestContext.Current.CancellationToken);
    }

    // ==========================================================================
    // Visibility (#241) and the hidden rule (#48)
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_ReturnsOnlyTheCallersVisibleAssets()
    {
        var library = new AssetLibrary();
        var own = library.Asset(library.MiraConfig);
        library.Asset(library.MiraConfig, softDeleted: true);
        library.Asset(library.MiraArchive);
        library.Asset(library.AliceConfig);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(own.Id);
    }

    [Fact]
    public async Task ReadAsync_AssetTheCallerHid_IsExcluded()
    {
        var library = new AssetLibrary();
        var hidden = library.Asset(library.MiraConfig);
        var kept = library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, hidden, hidden: true);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(kept.Id);
    }

    [Fact]
    public async Task ReadAsync_AssetAnotherUserHid_IsStillReturned()
    {
        var library = new AssetLibrary();
        var asset = library.Asset(library.MiraConfig);
        library.Interaction(library.Alice, asset, hidden: true);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(asset.Id);
    }

    [Fact]
    public async Task ReadAsync_AppliesTheSelectionAfterVisibility()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig);
        var wanted = library.Asset(library.MiraConfig);
        var alices = library.Asset(library.AliceConfig);

        (await ReadAsync(library, library.Mira, new OnlyAsset(wanted.Id))).Select(v => v.Id).Should().Equal(wanted.Id);
        (await ReadAsync(library, library.Mira, new OnlyAsset(alices.Id))).Should().BeEmpty();
    }

    // ==========================================================================
    // Interaction state
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_NoInteractionRow_IsNeitherStarredNorLiked()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.IsStarred.Should().BeFalse();
        view.IsLiked.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReadAsync_CallersInteraction_IsReported(bool starred, bool liked)
    {
        var library = new AssetLibrary();
        var asset = library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, asset, starred: starred, liked: liked);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.IsStarred.Should().Be(starred);
        view.IsLiked.Should().Be(liked);
    }

    [Fact]
    public async Task ReadAsync_AnotherUsersStarAndLike_AreNotShown()
    {
        var library = new AssetLibrary();
        var asset = library.Asset(library.MiraConfig);
        library.Interaction(library.Alice, asset, starred: true, liked: true);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.IsStarred.Should().BeFalse();
        view.IsLiked.Should().BeFalse();
    }

    // ==========================================================================
    // Asset fields
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_MapsTheAssetMetadataAndProxies()
    {
        var library = new AssetLibrary();
        var asset = library.Asset(
            library.MiraConfig,
            new LocalDateTime(2019, 7, 14, 9, 30),
            MediaType.Video,
            false,
            new Metadata { Description = MediaDescription.Unknown with { Width = 4032, Height = 3024, Orientation = 6, DurationInSeconds = 12 }, Blurhash = "LEHV6nWB2yk8" },
            null,
            ProxyType.Thumbnail,
            ProxyType.WebVideo);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.Should().Be(new AssetView(
            asset.Id, asset.ContentHash, MediaType.Video, 2019, 7, 14, new LocalDateTime(2019, 7, 14, 9, 30),
            4032, 3024, 6, 12, "LEHV6nWB2yk8",
            HasThumbnail: true, HasPreview: false, HasCompactPreview: false, HasVideo: true,
            PairedMovId: null, PairedMovContentHash: null, PairedMovHasVideo: false,
            IsStarred: false, IsLiked: false, BurstId: null, HiddenAt: null));
    }

    [Fact]
    public async Task ReadAsync_RowThatTookNewContent_KeepsItsIdAndReportsTheNewHash()
    {
        var library = new AssetLibrary();
        var asset = library.Asset(library.MiraConfig, proxyTypes: ProxyType.Thumbnail);
        asset.ContentHash = "0ld0ld0ld0ld0ld0";
        var options = await library.SeedAsync();
        await using (var db = new AnichronDbContext(options))
        {
            var row = await db.MediaAssets.Include(a => a.ProxyFiles).SingleAsync(TestContext.Current.CancellationToken);
            row.ContentHash = "n3wn3wn3wn3wn3wn";
            row.ProxyFiles.Single().ProxyPath = "n3/n3wn3wn3wn3wn3wn/thumbnail.jpg";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = new AnichronDbContext(options);
        var views = await new EfAssetViewReader(reading)
            .ReadAsync(library.Mira.Id, new Everything(), TestContext.Current.CancellationToken);

        var view = views.Should().ContainSingle().Which;
        (view.Id, view.ContentHash).Should().Be((asset.Id, "n3wn3wn3wn3wn3wn"));
    }

    [Fact]
    public async Task ReadAsync_PreviewProxy_IsReported()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig, null, MediaType.Image, false, null, proxyTypes: ProxyType.FullPreview);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        (view.HasThumbnail, view.HasPreview, view.HasCompactPreview, view.HasVideo).Should().Be((false, true, false, false));
    }

    [Fact]
    public async Task ReadAsync_CompactPreviewProxy_IsReported()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig, null, MediaType.Image, false, null, proxyTypes: ProxyType.CompactPreview);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        (view.HasThumbnail, view.HasPreview, view.HasCompactPreview, view.HasVideo).Should().Be((false, false, true, false));
    }

    [Fact]
    public async Task ReadAsync_NoProxies_ReportsNone()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        (view.HasThumbnail, view.HasPreview, view.HasCompactPreview, view.HasVideo).Should().Be((false, false, false, false));
    }

    [Fact]
    public async Task ReadAsync_NoMetadata_MapsToNulls()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.Width.Should().BeNull();
        view.Height.Should().BeNull();
        view.Orientation.Should().BeNull();
        view.DurationInSeconds.Should().BeNull();
        view.Blurhash.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_BurstMember_ReportsItsBurst()
    {
        var library = new AssetLibrary();
        var member = library.Asset(library.MiraConfig);
        var burst = library.Burst(member, library.Asset(library.MiraConfig));

        var views = await ReadAsync(library, library.Mira, new OnlyAsset(member.Id));

        views.Should().ContainSingle().Which.BurstId.Should().Be(burst.Id);
    }

    [Fact]
    public async Task ReadAsync_OutsideABurst_ReportsNoBurst()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig);

        var view = (await ReadAsync(library, library.Mira)).Should().ContainSingle().Which;

        view.BurstId.Should().BeNull();
    }

    // ==========================================================================
    // Live Photo pairing
    // ==========================================================================

    private const string MovHash = "cafebabecafebabe";

    private static async Task<AssetView> LivePhotoAsync(bool movSoftDeleted, params ProxyType[] movProxies)
    {
        var library = new AssetLibrary();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video, softDeleted: movSoftDeleted, proxyTypes: movProxies);
        mov.ContentHash = MovHash;
        var heic = library.LivePhoto(mov);

        var views = await ReadAsync(library, library.Mira, new OnlyAsset(heic.Id));
        return views.Should().ContainSingle().Which;
    }

    [Fact]
    public async Task ReadAsync_PairedMovWithVideo_ReportsItsIdHashAndVideo()
    {
        var view = await LivePhotoAsync(movSoftDeleted: false, ProxyType.WebVideo);

        view.PairedMovId.Should().NotBeNull();
        view.PairedMovContentHash.Should().Be(MovHash);
        view.PairedMovHasVideo.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_PairedMovWithoutVideo_ReportsItsIdButNoVideo()
    {
        var view = await LivePhotoAsync(movSoftDeleted: false, ProxyType.Thumbnail);

        view.PairedMovId.Should().NotBeNull();
        view.PairedMovHasVideo.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_SoftDeletedPairedMov_IsNotReported()
    {
        var view = await LivePhotoAsync(movSoftDeleted: true, ProxyType.WebVideo);

        view.PairedMovId.Should().BeNull();
        view.PairedMovContentHash.Should().BeNull();
        view.PairedMovHasVideo.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_PairedMov_IsNotListedOnItsOwn()
    {
        var library = new AssetLibrary();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video, proxyTypes: ProxyType.WebVideo);
        var heic = library.LivePhoto(mov);
        var video = library.Asset(library.MiraConfig, type: MediaType.Video);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().BeEquivalentTo([heic.Id, video.Id]);
        views.Single(v => v.Id == heic.Id).PairedMovId.Should().Be(mov.Id);
    }

    [Fact]
    public async Task ReadAsync_MovOfAHiddenLivePhoto_IsNotListedOnItsOwn()
    {
        var library = new AssetLibrary();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video);
        var heic = library.LivePhoto(mov);
        library.Interaction(library.Mira, heic, hidden: true);
        var kept = library.Asset(library.MiraConfig);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(kept.Id);
    }

    [Fact]
    public async Task ReadAsync_MovOfASoftDeletedLivePhoto_IsListedAsAVideo()
    {
        var library = new AssetLibrary();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video);
        library.LivePhoto(mov, softDeleted: true);

        var views = await ReadAsync(library, library.Mira);

        views.Select(v => v.Id).Should().Equal(mov.Id);
    }

    // ==========================================================================
    // BurstMembers (#299)
    // ==========================================================================

    [Fact]
    public async Task BurstMembers_ReturnsTheBurstsVisibleMembersOnly()
    {
        var library = new AssetLibrary();
        var first = library.Asset(library.MiraConfig);
        var second = library.Asset(library.MiraConfig);
        var hidden = library.Asset(library.MiraConfig);
        var deleted = library.Asset(library.MiraConfig, softDeleted: true);
        var burst = library.Burst(first, second, hidden, deleted);
        library.Interaction(library.Mira, hidden, hidden: true);
        library.Burst(library.Asset(library.MiraConfig), library.Asset(library.MiraConfig));
        library.Asset(library.MiraConfig);

        var views = await ReadAsync(library, library.Mira, new BurstMembers(burst.Id));

        views.Select(v => v.Id).Should().BeEquivalentTo([first.Id, second.Id]);
    }

    [Fact]
    public async Task BurstMembers_PairedMovInTheBurst_IsNotListed()
    {
        var library = new AssetLibrary();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video);
        var heic = library.LivePhoto(mov);
        var other = library.Asset(library.MiraConfig);
        var burst = library.Burst(heic, other, mov);

        var views = await ReadAsync(library, library.Mira, new BurstMembers(burst.Id));

        views.Select(v => v.Id).Should().BeEquivalentTo([heic.Id, other.Id]);
    }

    [Fact]
    public async Task BurstMembers_UnknownBurst_IsEmpty()
    {
        var library = new AssetLibrary();
        library.Burst(library.Asset(library.MiraConfig), library.Asset(library.MiraConfig));

        (await ReadAsync(library, library.Mira, new BurstMembers(Guid.NewGuid()))).Should().BeEmpty();
    }

    [Fact]
    public async Task BurstMembers_AnotherUsersBurst_IsEmpty()
    {
        var library = new AssetLibrary();
        var burst = library.Burst(library.Asset(library.AliceConfig), library.Asset(library.AliceConfig));

        (await ReadAsync(library, library.Mira, new BurstMembers(burst.Id))).Should().BeEmpty();
    }

    // ==========================================================================
    // HiddenAssets (#558)
    // ==========================================================================

    private static readonly Instant Monday = Instant.FromUtc(2026, 11, 16, 9, 0);

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static async Task<List<AssetView>> AllPagesAsync(AssetLibrary library, User caller, int take)
    {
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);
        var reader = new EfAssetViewReader(db);
        var all = new List<AssetView>();
        AssetCursor? after = null;
        for (var pages = 0; pages < 10; pages++)
        {
            var page = await reader.ReadAsync(caller.Id, new HiddenAssets(after, take), TestContext.Current.CancellationToken);
            all.AddRange(page);
            if (page.Count < take)
                return all;
            after = new AssetCursor(page[^1].HiddenAt!.Value, page[^1].Id);
        }

        throw new InvalidOperationException("The pages never ended.");
    }

    [Fact]
    public async Task HiddenAssets_ReturnsTheCallersHiddenAssetsNewestHiddenFirstWithWhenTheyWereHidden()
    {
        var library = new AssetLibrary();
        var older = library.Asset(library.MiraConfig);
        var newest = library.Asset(library.MiraConfig);
        var middle = library.Asset(library.MiraConfig);
        library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, older, hiddenAt: Monday);
        library.Interaction(library.Mira, newest, hiddenAt: Monday + Duration.FromDays(2));
        library.Interaction(library.Mira, middle, starred: true, hiddenAt: Monday + Duration.FromDays(1));

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 10));

        views.Select(v => (v.Id, v.HiddenAt)).Should().Equal(
            (newest.Id, Monday + Duration.FromDays(2)),
            (middle.Id, Monday + Duration.FromDays(1)),
            (older.Id, Monday));
        views[1].IsStarred.Should().BeTrue();
    }

    [Fact]
    public async Task HiddenAssets_SameHiddenTime_IsOrderedByIdDescending()
    {
        var library = new AssetLibrary();
        var first = library.Asset(library.MiraConfig);
        first.Id = Id(1);
        var second = library.Asset(library.MiraConfig);
        second.Id = Id(2);
        library.Interaction(library.Mira, first, hiddenAt: Monday);
        library.Interaction(library.Mira, second, hiddenAt: Monday);

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 10));

        views.Select(v => v.Id).Should().Equal(Id(2), Id(1));
    }

    [Fact]
    public async Task HiddenAssets_TakesAtMostThePageSize()
    {
        var library = new AssetLibrary();
        foreach (var day in Enumerable.Range(0, 3))
            library.Interaction(library.Mira, library.Asset(library.MiraConfig), hiddenAt: Monday + Duration.FromDays(day));

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 2));

        views.Select(v => v.HiddenAt).Should().Equal(Monday + Duration.FromDays(2), Monday + Duration.FromDays(1));
    }

    [Fact]
    public async Task HiddenAssets_AfterACursor_StartsWithTheNextRowInOrder()
    {
        var library = new AssetLibrary();
        foreach (var n in Enumerable.Range(1, 4))
        {
            var asset = library.Asset(library.MiraConfig);
            asset.Id = Id(n);
            library.Interaction(library.Mira, asset, hiddenAt: n == 4 ? Monday - Duration.FromHours(1) : Monday);
        }

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(new AssetCursor(Monday, Id(2)), Take: 10));

        views.Select(v => v.Id).Should().Equal(Id(1), Id(4));
    }

    [Fact]
    public async Task HiddenAssets_KeysetPages_CoverTheSetWithoutGapsOrRepeats()
    {
        var library = new AssetLibrary();
        int[] hoursAfterMonday = [0, 0, 2, 2, 2, 1, 1];
        foreach (var (n, hours) in hoursAfterMonday.Select((hours, index) => (index + 1, hours)))
        {
            var asset = library.Asset(library.MiraConfig);
            asset.Id = Id(n);
            library.Interaction(library.Mira, asset, hiddenAt: Monday + Duration.FromHours(hours));
        }

        var all = await AllPagesAsync(library, library.Mira, take: 2);

        all.Select(v => v.Id).Should().Equal(Id(5), Id(4), Id(3), Id(7), Id(6), Id(2), Id(1));
    }

    [Fact]
    public async Task HiddenAssets_LeavesOutWhatIsNotTheCallersVisibleDatedHiddenAsset()
    {
        var library = new AssetLibrary();
        var hidden = library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, hidden, hidden: true);
        library.Asset(library.MiraConfig);
        library.Interaction(library.Mira, library.Asset(library.MiraConfig), starred: true);
        library.Interaction(library.Alice, library.Asset(library.MiraConfig), hidden: true);
        library.Interaction(library.Alice, library.Asset(library.AliceConfig), hidden: true);
        library.Interaction(library.Mira, library.Asset(library.MiraArchive), hidden: true);
        library.Interaction(library.Mira, library.Asset(library.MiraConfig, softDeleted: true), hidden: true);
        var undated = library.Asset(library.MiraConfig);
        undated.Captured = null;
        library.Interaction(library.Mira, undated, hidden: true);

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 10));

        views.Select(v => v.Id).Should().Equal(hidden.Id);
    }

    [Fact]
    public async Task HiddenAssets_HiddenLivePhoto_IsListedWithoutItsPairedMov()
    {
        var library = new AssetLibrary();
        var mov = library.Asset(library.MiraConfig, type: MediaType.Video);
        var heic = library.LivePhoto(mov);
        library.Interaction(library.Mira, heic, hidden: true);
        library.Interaction(library.Mira, mov, hidden: true);

        var views = await ReadAsync(library, library.Mira, new HiddenAssets(After: null, Take: 10));

        var view = views.Should().ContainSingle().Which;
        (view.Id, view.PairedMovId).Should().Be((heic.Id, mov.Id));
    }

    [Fact]
    public async Task ReadAsync_ShownSet_ReportsNoHiddenTime()
    {
        var library = new AssetLibrary();
        library.Interaction(library.Mira, library.Asset(library.MiraConfig), starred: true);

        var views = await ReadAsync(library, library.Mira);

        views.Should().ContainSingle().Which.HiddenAt.Should().BeNull();
    }

    // ==========================================================================
    // Generated SQL
    // ==========================================================================

    [Fact]
    public void Query_HiddenAssets_PagesByKeysetInSqlAndKeepsTheOrderOutermost()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new HiddenAssets(new AssetCursor(Monday, Id(1)), 61)).ToQueryString();

        sql.Should().MatchRegex(@"\.""HiddenAt"" < @cursor_At OR \(\w+\.""HiddenAt"" = @cursor_At AND \w+\.""Id"" < @cursor_AssetId\)")
            .And.MatchRegex(@"ORDER BY \w+\.""HiddenAt"" DESC, \w+\.""Id"" DESC\s+LIMIT @p")
            .And.MatchRegex(@"ORDER BY \w+\.""HiddenAt"" DESC, \w+\.""Id"" DESC$", "the joins around the page must not lose its order")
            .And.Contain("\"HiddenAt\" IS NOT NULL")
            .And.NotContain(";");
    }

    [Fact]
    public void Query_HiddenAssets_FirstPageHasNoKeyset()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new HiddenAssets(After: null, 61)).ToQueryString();

        sql.Should().NotContain("@cursor").And.Contain("LIMIT @p");
    }

    [Fact]
    public void Query_BurstMembers_FiltersOnTheBurstAfterVisibility()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new BurstMembers(Guid.NewGuid())).ToQueryString();

        sql.Should().Contain("\"BurstId\" = @BurstId")
            .And.Contain("\"IsActive\"")
            .And.Contain("\"HiddenAt\" IS NOT NULL")
            .And.NotContain(";");
    }

    [Fact]
    public void Query_BurstMembers_ExcludesPairedMovsInTheSameStatement()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new BurstMembers(Guid.NewGuid())).ToQueryString();

        PairedMovExclusions(sql).Should().Be(1);
        sql.Should().NotContain(";");
    }

    private static int PairedMovExclusions(string sql) => PairedMovExclusion().Count(sql);

    [GeneratedRegex(@"""PairedAssetId"" = \w+\.""Id""\)")]
    private static partial Regex PairedMovExclusion();

    [Fact]
    public void Query_FiltersVisibilityTheHiddenRuleAndTheInteractionJoinOnTheCaller()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfAssetViewReader.Query(db, Guid.NewGuid(), new Everything()).ToQueryString();

        sql.Should().Contain("JOIN \"StorageConfigs\"")
            .And.Contain("\"IsActive\"")
            .And.Contain("\"IsSoftDeleted\"")
            .And.Contain("\"HiddenAt\" IS NOT NULL")
            .And.Contain("LEFT JOIN")
            .And.Contain("FROM \"Interactions\"");
        sql.Split("\"UserId\" = @").Should().HaveCount(5,
            "the owned-config check, the hidden rule, the paired-MOV exclusion and the interaction join each filter on the caller");
    }

    [Fact]
    public async Task ReadAsync_IsNotTracked()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig);
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);

        await new EfAssetViewReader(db).ReadAsync(library.Mira.Id, new Everything(), TestContext.Current.CancellationToken);

        db.ChangeTracker.Entries().Should().BeEmpty();
    }
}
