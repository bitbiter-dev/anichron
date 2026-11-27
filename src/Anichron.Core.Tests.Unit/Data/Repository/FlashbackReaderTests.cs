using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Testing.Domain;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class FlashbackReaderTests
{
    private static readonly LocalDate July14 = new(2026, 7, 14);

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static async Task<IReadOnlyList<FlashbackYear>> DayAsync(AssetLibrary library, LocalDate date, User? caller = null)
    {
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);
        return await new EfFlashbackReader(db)
            .ReadDayAsync((caller ?? library.Mira).Id, date, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<RecentDay>> RecentAsync(
        AssetLibrary library, LocalDate before, LocalDate? since = null, int limit = 366)
    {
        var options = await library.SeedAsync();
        await using var db = new AnichronDbContext(options);
        return await new EfFlashbackReader(db)
            .ReadRecentDaysAsync(library.Mira.Id, before, since, limit, TestContext.Current.CancellationToken);
    }

    private static FlashbackItem Item(LocalDate day, int year, int id = 1, bool starred = false)
        => new()
        {
            Id = Id(id),
            Year = year,
            Month = day.Month,
            Day = day.Day,
            DateCaptured = new LocalDate(year, day.Month, day.Day).AtMidnight(),
            IsStarred = starred,
            IsLiked = false,
            BurstId = null,
            BurstCount = 0,
        };

    private static IEnumerable<Guid> Ids(IReadOnlyList<FlashbackYear> years)
        => years.SelectMany(year => year.Memories).Select(memory => memory.View.Id);

    private static FlashbackMemory Single(IReadOnlyList<FlashbackYear> years)
        => years.Should().ContainSingle().Which.Memories.Should().ContainSingle().Subject;

    // ==========================================================================
    // The day (#38)
    // ==========================================================================

    [Fact]
    public async Task ReadDayAsync_ReturnsTheSameDayInEarlierYearsOnly()
    {
        var library = new AssetLibrary();
        var earlier = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        var muchEarlier = library.Asset(library.MiraConfig, new LocalDateTime(2003, 7, 14, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2026, 7, 14, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2027, 7, 14, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 8, 14, 9, 0));

        var years = await DayAsync(library, July14);

        years.Select(year => (year.Year, year.Memories.Single().View.Id))
            .Should().Equal((2019, earlier.Id), (2003, muchEarlier.Id));
    }

    [Fact]
    public async Task ReadDayAsync_ReadsOnlyTheCallersVisibleShownAssets()
    {
        var library = new AssetLibrary();
        var mine = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        library.Asset(library.AliceConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        library.Asset(library.MiraArchive, new LocalDateTime(2019, 7, 14, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0), softDeleted: true);
        var hidden = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        library.Interaction(library.Mira, hidden, hidden: true);

        var years = await DayAsync(library, July14);

        Ids(years).Should().Equal(mine.Id);
    }

    [Theory]
    [InlineData(2025, 2, 28, true)]
    [InlineData(2027, 2, 28, true)]
    [InlineData(2028, 2, 29, true)]
    [InlineData(2028, 2, 28, false)]
    [InlineData(2025, 3, 1, false)]
    [InlineData(2027, 2, 27, false)]
    public async Task ReadDayAsync_TwentyNinthOfFebruary_IsMatchedOnALeapDayAndOnTheTwentyEighthOfOtherYears(
        int year, int month, int day, bool matched)
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig, new LocalDateTime(2024, 2, 29, 9, 0));

        var years = await DayAsync(library, new LocalDate(year, month, day));

        Ids(years).Should().HaveCount(matched ? 1 : 0);
    }

    [Fact]
    public async Task ReadDayAsync_TwentyEighthOfFebruaryInAYearWithoutALeapDay_BringsBothDaysEachInItsYear()
    {
        var library = new AssetLibrary();
        var leapDay = library.Asset(library.MiraConfig, new LocalDateTime(2024, 2, 29, 9, 0));
        var twentyEighth = library.Asset(library.MiraConfig, new LocalDateTime(2025, 2, 28, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2025, 2, 27, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2025, 3, 1, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2024, 3, 29, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2024, 1, 29, 9, 0));

        var years = await DayAsync(library, new LocalDate(2027, 2, 28));

        years.Select(year => (year.Year, year.Memories.Single().View.Id))
            .Should().Equal((2025, twentyEighth.Id), (2024, leapDay.Id));
    }

    [Theory]
    [InlineData(3, 28, 29)]
    [InlineData(2, 14, 15)]
    public async Task ReadDayAsync_AnotherDayInAYearWithoutALeapDay_DoesNotBringTheNextDay(int month, int day, int nextDay)
    {
        var library = new AssetLibrary();
        var onTheDay = library.Asset(library.MiraConfig, new LocalDateTime(2019, month, day, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, month, nextDay, 9, 0));

        var years = await DayAsync(library, new LocalDate(2027, month, day));

        Ids(years).Should().Equal(onTheDay.Id);
    }

    [Fact]
    public async Task ReadDayAsync_LivePhoto_IsListedOnceWithItsMov()
    {
        var library = new AssetLibrary();
        var onTheDay = new LocalDateTime(2019, 7, 14, 9, 0);
        var mov = library.Asset(library.MiraConfig, onTheDay, MediaType.Video, proxyTypes: ProxyType.WebVideo);
        var heic = library.LivePhoto(mov, onTheDay);
        var video = library.Asset(library.MiraConfig, onTheDay, MediaType.Video);

        var years = await DayAsync(library, July14);

        Ids(years).Should().BeEquivalentTo([heic.Id, video.Id]);
        var livePhoto = years.SelectMany(year => year.Memories).Single(memory => memory.View.Id == heic.Id).View;
        (livePhoto.PairedMovId, livePhoto.PairedMovHasVideo).Should().Be((mov.Id, true));
    }

    [Fact]
    public async Task ReadDayAsync_MovWhoseLivePhotoRowIsGone_IsListedAsAVideo()
    {
        var library = new AssetLibrary();
        var mov = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0), MediaType.Video);

        var view = Single(await DayAsync(library, July14)).View;

        (view.Id, view.MediaType).Should().Be((mov.Id, MediaType.Video));
    }

    [Fact]
    public async Task ReadDayAsync_ReturnsTheFullViewWithTheCallersInteraction()
    {
        var library = new AssetLibrary();
        var photo = library.Asset(
            library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0), MediaType.Image, false, null, null, ProxyType.Thumbnail, ProxyType.FullPreview);
        library.Interaction(library.Mira, photo, starred: true, liked: true);
        library.Interaction(library.Alice, photo);

        var memory = Single(await DayAsync(library, July14));

        memory.View.Should().BeEquivalentTo(new
        {
            photo.Id,
            photo.ContentHash,
            Year = 2019,
            Month = 7,
            Day = 14,
            HasThumbnail = true,
            HasPreview = true,
            HasCompactPreview = false,
            IsStarred = true,
            IsLiked = true,
            BurstId = (Guid?)null,
        });
        memory.Burst.Should().BeNull();
    }

    // ==========================================================================
    // Order (#38)
    // ==========================================================================

    [Fact]
    public async Task ReadDayAsync_WithinAYear_PutsStarredFirstThenCaptureOrderThenId()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 18, 0), id: Id(1));
        var starredLate = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 20, 0), id: Id(2));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 8, 0), id: Id(3));
        var starredNoon = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 12, 0), id: Id(4));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 8, 0), id: Id(5));
        library.Interaction(library.Mira, starredLate, starred: true);
        library.Interaction(library.Mira, starredNoon, starred: true);

        var years = await DayAsync(library, July14);

        Ids(years).Should().Equal(Id(4), Id(2), Id(3), Id(5), Id(1));
    }

    // ==========================================================================
    // Bursts (#299)
    // ==========================================================================

    [Fact]
    public async Task ReadDayAsync_Burst_IsOneMemoryShowingItsEarliestMemberWithTheMemberCount()
    {
        var library = new AssetLibrary();
        var cover = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0, 0), id: Id(2));
        var burst = library.Burst(
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0, 2), id: Id(3)),
            cover,
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0, 1), id: Id(1)));

        var memory = Single(await DayAsync(library, July14));

        (memory.View.Id, memory.View.BurstId, memory.Burst).Should().Be((cover.Id, burst.Id, new FlashbackBurst(burst.Id, 3)));
    }

    [Fact]
    public async Task ReadDayAsync_BurstMembersCapturedTogether_CoverIsTheLowestId()
    {
        var library = new AssetLibrary();
        library.Burst(
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0), id: Id(0xb)),
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0), id: Id(0xa)));

        Single(await DayAsync(library, July14)).View.Id.Should().Be(Id(0xa));
    }

    [Fact]
    public async Task ReadDayAsync_TwoBurstsAndASinglePhoto_AreThreeMemories()
    {
        var library = new AssetLibrary();
        var first = library.Burst(
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0), id: Id(1)),
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 1), id: Id(2)));
        var second = library.Burst(
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 10, 0), id: Id(3)),
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 10, 1), id: Id(4)));
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 11, 0), id: Id(5));

        var years = await DayAsync(library, July14);

        years.Should().ContainSingle().Which.Memories.Select(memory => (memory.View.Id, memory.Burst)).Should().Equal(
            (Id(1), new FlashbackBurst(first.Id, 2)),
            (Id(3), new FlashbackBurst(second.Id, 2)),
            (Id(5), null));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReadDayAsync_BurstWithAStarredOrLikedMember_IsStarredOrLikedAndAStarredOneSortsFirst(bool starred, bool liked)
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 8, 0), id: Id(1));
        var member = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 1), id: Id(3));
        library.Burst(library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0), id: Id(2)), member);
        library.Interaction(library.Mira, member, starred: starred, liked: liked);

        var years = await DayAsync(library, July14);

        var memories = years.Should().ContainSingle().Which.Memories;
        memories.Select(memory => memory.View.Id).Should().Equal(starred ? [Id(2), Id(1)] : [Id(1), Id(2)]);
        var cover = memories.Single(memory => memory.View.Id == Id(2)).View;
        (cover.IsStarred, cover.IsLiked).Should().Be((starred, liked));
    }

    [Fact]
    public async Task ReadDayAsync_BurstWithoutStarsOrLikes_IsNeitherEvenWhenAnotherUserStarredIt()
    {
        var library = new AssetLibrary();
        var cover = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        var member = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 1));
        library.Burst(cover, member);
        library.Interaction(library.Alice, member, starred: true, liked: true);

        var view = Single(await DayAsync(library, July14)).View;

        (view.IsStarred, view.IsLiked).Should().Be((false, false));
    }

    [Fact]
    public async Task ReadDayAsync_BurstAcrossMidnight_BelongsToTheDayOfItsCoverWithEveryMemberCounted()
    {
        var library = new AssetLibrary();
        var beforeMidnight = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 23, 59));
        var burst = library.Burst(beforeMidnight, library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 0, 1)));

        var onTheDay = Single(await DayAsync(library, July14));
        var nextDay = await DayAsync(library, July14.PlusDays(1));

        (onTheDay.View.Id, onTheDay.Burst).Should().Be((beforeMidnight.Id, new FlashbackBurst(burst.Id, 2)));
        nextDay.Should().BeEmpty();
    }

    [Theory]
    [InlineData(2027, 2, 28, true)]
    [InlineData(2028, 2, 29, true)]
    [InlineData(2027, 3, 1, false)]
    public async Task ReadDayAsync_BurstCoveredOnTheTwentyNinthOfFebruary_IsOnTheDaysThatFoldIt(
        int year, int month, int day, bool shown)
    {
        var library = new AssetLibrary();
        var cover = library.Asset(library.MiraConfig, new LocalDateTime(2024, 2, 29, 23, 59));
        library.Burst(cover, library.Asset(library.MiraConfig, new LocalDateTime(2024, 3, 1, 0, 1)));

        var years = await DayAsync(library, new LocalDate(year, month, day));

        Ids(years).Should().Equal(shown ? [cover.Id] : []);
    }

    [Fact]
    public async Task ReadDayAsync_BurstStartingOnTheSameDayOfAnEarlierMonth_IsNotShown()
    {
        var library = new AssetLibrary();
        library.Burst(
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 6, 14, 9, 0)),
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0)));

        (await DayAsync(library, July14)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadDayAsync_LabelledBurstSpanningWeeks_CountsEveryShownMemberOnItsCoversDay()
    {
        var library = new AssetLibrary();
        var cover = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        var hidden = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 20, 9, 0));
        var burst = library.Burst(
            cover,
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 28, 9, 0)),
            hidden,
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 8, 4, 9, 0)));
        library.Interaction(library.Mira, hidden, hidden: true);

        var onTheCoversDay = Single(await DayAsync(library, July14));
        var onAMembersDay = await DayAsync(library, new LocalDate(2026, 8, 4));

        (onTheCoversDay.View.Id, onTheCoversDay.Burst).Should().Be((cover.Id, new FlashbackBurst(burst.Id, 3)));
        onAMembersDay.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadDayAsync_UndatedBurstMember_IsNeitherTheCoverNorCounted()
    {
        var library = new AssetLibrary();
        var dated = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 9, 0));
        var undated = library.Asset(library.MiraConfig);
        undated.Captured = null;
        var burst = library.Burst(undated, dated);

        var memory = Single(await DayAsync(library, July14));

        (memory.View.Id, memory.Burst).Should().Be((dated.Id, new FlashbackBurst(burst.Id, 1)));
    }

    [Theory]
    [InlineData(2026, 7, 14)]
    [InlineData(2027, 7, 14)]
    [InlineData(2019, 8, 14)]
    [InlineData(2019, 7, 15)]
    public async Task ReadDayAsync_BurstWithoutAMemberOnTheDayInAnEarlierYear_IsNotBroughtIn(int year, int month, int day)
    {
        var library = new AssetLibrary();
        library.Burst(
            library.Asset(library.MiraConfig, new LocalDateTime(year, month, day, 12, 0)),
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 16, 12, 0)));

        (await DayAsync(library, July14)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadDayAsync_OnlyMemberOnTheDayIsHidden_DoesNotBringItsBurst()
    {
        var library = new AssetLibrary();
        var hidden = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 23, 59));
        library.Burst(hidden, library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 0, 1)));
        library.Interaction(library.Mira, hidden, hidden: true);

        (await DayAsync(library, July14)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadDayAsync_EveryMemberBeforeMidnightHidden_BringsTheBurstOnTheNextDay()
    {
        var library = new AssetLibrary();
        var hidden = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 23, 59));
        var afterMidnight = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 15, 0, 1));
        var burst = library.Burst(hidden, afterMidnight);
        library.Interaction(library.Mira, hidden, hidden: true);

        var memory = Single(await DayAsync(library, July14.PlusDays(1)));

        (memory.View.Id, memory.Burst).Should().Be((afterMidnight.Id, new FlashbackBurst(burst.Id, 1)));
    }

    [Fact]
    public async Task ReadDayAsync_SoftDeletedCover_GivesTheBurstToItsNextMember()
    {
        var library = new AssetLibrary();
        var removed = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 13, 23, 59), softDeleted: true);
        var afterMidnight = library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 14, 0, 1));
        library.Burst(removed, afterMidnight);

        Single(await DayAsync(library, July14)).View.Id.Should().Be(afterMidnight.Id);
    }

    // ==========================================================================
    // The rule
    // ==========================================================================

    [Theory]
    [InlineData(2019, 7, 14, true)]
    [InlineData(2025, 7, 14, true)]
    [InlineData(2026, 7, 14, false)]
    [InlineData(2027, 7, 14, false)]
    [InlineData(2019, 7, 13, false)]
    [InlineData(2019, 7, 15, false)]
    [InlineData(2019, 6, 14, false)]
    public void OnThisDay_Includes_TheSameDayAndMonthInAnEarlierYear(int year, int month, int day, bool included)
    {
        var item = new FlashbackItem
        {
            Id = Id(1),
            Year = year,
            Month = month,
            Day = day,
            DateCaptured = new LocalDateTime(year, month, day, 9, 0),
            IsStarred = false,
            IsLiked = false,
            BurstId = null,
            BurstCount = 0,
        };

        new OnThisDay(July14).Includes().Compile()(item).Should().Be(included);
    }

    // ==========================================================================
    // Recent days (#631)
    // ==========================================================================

    [Fact]
    public async Task ReadRecentDaysAsync_ReturnsTheDaysWithMemoriesNewestFirstWithTheirNewestYearsFirstMemory()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 13, 8, 0), id: Id(1));
        library.Asset(library.MiraConfig, new LocalDateTime(2021, 7, 13, 18, 0), id: Id(2));
        var starred = library.Asset(library.MiraConfig, new LocalDateTime(2021, 7, 13, 19, 0), id: Id(3));
        library.Interaction(library.Mira, starred, starred: true);
        var tenth = library.Asset(library.MiraConfig, new LocalDateTime(2015, 7, 10, 8, 0), id: Id(4));
        library.Asset(library.MiraConfig, new LocalDateTime(2026, 7, 12, 8, 0));

        var days = await RecentAsync(library, July14, since: new LocalDate(2026, 7, 1));

        days.Select(day => (day.Date, day.Year, day.Memory.View.Id)).Should().Equal(
            (new LocalDate(2026, 7, 13), 2021, starred.Id),
            (new LocalDate(2026, 7, 10), 2015, tenth.Id));
        days[0].Memory.View.IsStarred.Should().BeTrue();
    }

    [Fact]
    public async Task ReadRecentDaysAsync_ReturnsAtMostTheLimitNewestFirst()
    {
        var library = new AssetLibrary();
        foreach (var day in new[] { 13, 11, 9, 2 })
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, day, 9, 0));

        var days = await RecentAsync(library, July14, limit: 3);

        days.Select(day => day.Date.Day).Should().Equal(13, 11, 9);
    }

    [Fact]
    public async Task ReadRecentDaysAsync_ReadsFromSinceUpToTheDayBeforeBefore()
    {
        var library = new AssetLibrary();
        foreach (var day in new[] { 15, 14, 13, 9, 8 })
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, day, 9, 0));

        var days = await RecentAsync(library, July14, since: new LocalDate(2026, 7, 9));

        days.Select(day => day.Date.Day).Should().Equal(13, 9);
    }

    [Fact]
    public async Task ReadRecentDaysAsync_ReadsBackAYearOf366DaysAtMost()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig, new LocalDateTime(2024, 7, 13, 9, 0));
        library.Asset(library.MiraConfig, new LocalDateTime(2024, 7, 12, 9, 0));

        var days = await RecentAsync(library, July14, since: new LocalDate(2020, 1, 1));

        days.Select(day => day.Date).Should().Equal(
            new LocalDate(2026, 7, 13), new LocalDate(2026, 7, 12), new LocalDate(2025, 7, 13));
    }

    [Fact]
    public async Task ReadRecentDaysAsync_SinceOnOrAfterBefore_IsEmpty()
    {
        var library = new AssetLibrary();
        library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 13, 9, 0));

        (await RecentAsync(library, July14, since: July14)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadRecentDaysAsync_BurstStraddlingSince_IsNotADayWhenItsCoverIsBeforeSince()
    {
        var library = new AssetLibrary();
        library.Burst(
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 9, 23, 59)),
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 10, 0, 1)));

        (await RecentAsync(library, July14, since: new LocalDate(2026, 7, 10))).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadRecentDaysAsync_Burst_IsCountedOnItsCoversDayWithEveryShownMember()
    {
        var library = new AssetLibrary();
        var cover = library.Asset(library.MiraConfig, new LocalDateTime(2019, 6, 20, 9, 0));
        var burst = library.Burst(
            cover,
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 10, 9, 0)),
            library.Asset(library.MiraConfig, new LocalDateTime(2019, 7, 12, 9, 0)));

        var days = await RecentAsync(library, July14, since: new LocalDate(2026, 6, 1));

        var day = days.Should().ContainSingle().Subject;
        (day.Date, day.Memory.View.Id, day.Memory.Burst).Should().Be(
            (new LocalDate(2026, 6, 20), cover.Id, new FlashbackBurst(burst.Id, 3)));
    }

    [Fact]
    public async Task ReadRecentDaysAsync_IsEveryDateWhoseDayHasMemoriesStartingOnTheDaysFirstMemory()
    {
        var library = Fixtures();
        var before = new LocalDate(2026, 3, 5);

        foreach (var since in new LocalDate?[] { null, new LocalDate(2026, 1, 10) })
        {
            var recent = await RecentAsync(library, before, since);
            var expected = new List<(LocalDate, int, FlashbackMemory)>();
            for (var date = before.PlusDays(-1); date >= (since ?? before.PlusDays(-366)); date = date.PlusDays(-1))
            {
                var day = await DayAsync(library, date);
                if (day.Count > 0)
                    expected.Add((date, day[0].Year, day[0].Memories[0]));
            }

            expected.Should().HaveCountGreaterThan(4, "the fixtures put memories on many days of the window");
            recent.Select(day => (day.Date, day.Year, day.Memory)).Should().Equal(expected, $"since {since}");
        }
    }

    private static AssetLibrary Fixtures()
    {
        var library = new AssetLibrary();
        var config = library.MiraConfig;
        library.Asset(config, new LocalDateTime(2024, 2, 29, 9, 0));
        library.Asset(config, new LocalDateTime(2023, 2, 28, 7, 0));
        library.Asset(config, new LocalDateTime(2025, 1, 1, 0, 10));
        library.Asset(config, new LocalDateTime(2024, 12, 31, 23, 50));
        library.Asset(config, new LocalDateTime(2020, 12, 31, 12, 0));
        library.Burst(
            library.Asset(config, new LocalDateTime(2019, 1, 9, 23, 59)),
            library.Asset(config, new LocalDateTime(2019, 1, 10, 0, 1)));
        library.Burst(
            library.Asset(config, new LocalDateTime(2018, 2, 3, 23, 58)),
            library.Asset(config, new LocalDateTime(2018, 2, 4, 0, 2)),
            library.Asset(config, new LocalDateTime(2018, 2, 4, 0, 3)));
        library.Burst(
            library.Asset(config, new LocalDateTime(2017, 1, 15, 9, 0)),
            library.Asset(config, new LocalDateTime(2017, 2, 1, 9, 0)),
            library.Asset(config, new LocalDateTime(2017, 2, 20, 9, 0)));
        var hiddenCover = library.Asset(config, new LocalDateTime(2016, 1, 19, 23, 0));
        library.Burst(hiddenCover, library.Asset(config, new LocalDateTime(2016, 1, 20, 1, 0)));
        library.Interaction(library.Mira, hiddenCover, hidden: true);
        var hidden = library.Asset(config, new LocalDateTime(2022, 2, 10, 9, 0));
        library.Interaction(library.Mira, hidden, hidden: true);
        library.Asset(config, new LocalDateTime(2022, 2, 11, 9, 0), softDeleted: true);
        var starred = library.Asset(config, new LocalDateTime(2015, 2, 12, 20, 0));
        library.Asset(config, new LocalDateTime(2015, 2, 12, 8, 0));
        library.Asset(config, new LocalDateTime(2021, 2, 12, 9, 0));
        library.Interaction(library.Mira, starred, starred: true);
        library.Asset(config, new LocalDateTime(2025, 3, 4, 9, 0));
        library.Asset(config, new LocalDateTime(2026, 3, 1, 9, 0));
        library.Asset(config, new LocalDateTime(2019, 11, 2, 9, 0));
        library.Asset(config, new LocalDateTime(2018, 6, 30, 9, 0));
        library.Asset(config, new LocalDateTime(2010, 3, 5, 9, 0));
        return library;
    }

    // ==========================================================================
    // Windows (#631)
    // ==========================================================================

    [Fact]
    public async Task WalkAsync_FirstWindowFillsTheLimit_ReadsOnlyThe31DaysBeforeBefore()
    {
        var read = new List<DayWindow>();

        var found = await EfFlashbackReader.WalkAsync(July14, since: null, limit: 1, window =>
        {
            read.Add(window);
            return Task.FromResult<IReadOnlyList<FlashbackItem>>([Item(July14.PlusDays(-3), 2019)]);
        });

        read.Should().Equal(new DayWindow(July14.PlusDays(-31), July14));
        found.Select(first => first.Date).Should().Equal(July14.PlusDays(-3));
    }

    [Fact]
    public async Task WalkAsync_FirstWindowShort_ReadsTheRestOfTheYearAfterIt()
    {
        var read = new List<DayWindow>();

        var found = await EfFlashbackReader.WalkAsync(July14, since: null, limit: 2, window =>
        {
            read.Add(window);
            return Task.FromResult<IReadOnlyList<FlashbackItem>>(
                [Item(July14.PlusDays(-3), 2019), Item(July14.PlusDays(-200), 2018, id: 2)]);
        });

        read.Should().Equal(
            new DayWindow(July14.PlusDays(-31), July14),
            new DayWindow(July14.PlusDays(-366), July14.PlusDays(-31)));
        found.Select(first => first.Date).Should().Equal(July14.PlusDays(-3), July14.PlusDays(-200));
    }

    [Fact]
    public async Task WalkAsync_SinceWithinTheFirstWindow_ReadsOnlyFromSince()
    {
        var read = new List<DayWindow>();

        await EfFlashbackReader.WalkAsync(July14, since: July14.PlusDays(-6), limit: 6, window =>
        {
            read.Add(window);
            return Task.FromResult<IReadOnlyList<FlashbackItem>>([]);
        });

        read.Should().Equal(new DayWindow(July14.PlusDays(-6), July14));
    }

    [Fact]
    public async Task WalkAsync_SinceOnOrAfterBefore_ReadsNothing()
    {
        var read = new List<DayWindow>();

        var found = await EfFlashbackReader.WalkAsync(July14, since: July14, limit: 6, window =>
        {
            read.Add(window);
            return Task.FromResult<IReadOnlyList<FlashbackItem>>([]);
        });

        read.Should().BeEmpty();
        found.Should().BeEmpty();
    }

    [Fact]
    public async Task WalkAsync_SeveralYearsOnADate_TakesTheNewestYearsFirstInTheDaysOrder()
    {
        var day = July14.PlusDays(-1);

        var found = await EfFlashbackReader.WalkAsync(July14, since: day, limit: 1, _ =>
            Task.FromResult<IReadOnlyList<FlashbackItem>>(
                [Item(day, 2015, id: 1, starred: true), Item(day, 2021, id: 2), Item(day, 2021, id: 3, starred: true)]));

        found.Should().ContainSingle().Which.First.Id.Should().Be(Id(3));
    }

    [Theory]
    [InlineData("2026-12-25", "2027-01-01")]
    [InlineData("2026-12-24", "2026-12-31")]
    public void Covering_AnAssetOnAWindowDateInItsOwnYear_IsLeftOut(string start, string end)
    {
        var window = new DayWindow(LocalDatePattern.Iso.Parse(start).Value, LocalDatePattern.Iso.Parse(end).Value);
        var covers = window.Covering().Compile();

        covers(new MediaAsset { Captured = Recorded.At(new LocalDateTime(2026, 12, 28, 12, 0)) }).Should().BeFalse();
        covers(new MediaAsset { Captured = Recorded.At(new LocalDateTime(2025, 12, 28, 12, 0)) }).Should().BeTrue();
    }

    // ==========================================================================
    // Generated SQL
    // ==========================================================================

    private static string DaySql(LocalDate date)
    {
        using var db = AnichronDbContexts.NpgsqlModel();
        return EfFlashbackReader.Items(db, Guid.NewGuid(), DayWindow.Of(date)).Where(new OnThisDay(date).Includes()).ToQueryString();
    }

    private static string WindowSql(DayWindow window)
    {
        using var db = AnichronDbContexts.NpgsqlModel();
        return EfFlashbackReader.Items(db, Guid.NewGuid(), window).ToQueryString();
    }

    [Fact]
    public void Query_Day_IsOneStatementOnTheIndexedDayColumnsWithTheYearTest()
    {
        var sql = DaySql(July14);

        sql.Should().MatchRegex(@"""Month"" = @month\w* AND \w+\.""Day"" >= @first\w* AND \w+\.""Day"" <= @last\w*\)? AND \w+\.""Year"" < @lastYear")
            .And.Contain("@month='7'").And.Contain("@first='14'").And.Contain("@last='14'").And.Contain("@lastYear='2026'")
            .And.MatchRegex(@"""Year"" < @year\b").And.Contain("@year='2026'")
            .And.NotContain("EXTRACT")
            .And.NotContain(";");
    }

    [Fact]
    public void Query_Day_ChoosesEachBurstsCoverInSqlFromItsShownMembersFoundByBurstId()
    {
        var sql = DaySql(July14);

        sql.Should().Contain("UNION ALL")
            .And.MatchRegex(@"GROUP BY \w+\.""BurstId""")
            .And.MatchRegex(@"NOT EXISTS \(\s+SELECT 1\s+FROM ""MediaAssets"" AS \w+[\s\S]*\(\w+\.""DateCaptured"" < \w+\.""DateCaptured"" OR \(\w+\.""DateCaptured"" = \w+\.""DateCaptured"" AND \w+\.""Id"" < \w+\.""Id""\)\)")
            .And.MatchRegex(@"max\(CASE[\s\S]*""IsStarred""[\s\S]*max\(CASE[\s\S]*""IsLiked""")
            .And.Contain("count(*)::int");
    }

    [Fact]
    public void Query_Window_NarrowsOnEachMonthsDaysAndTheYearBeforeItsLastDay()
    {
        var sql = WindowSql(new DayWindow(new LocalDate(2026, 6, 13), July14));

        sql.Should().MatchRegex(@"\(\w+\.""Month"" = @month\w* AND \w+\.""Day"" >= @first\w* AND \w+\.""Day"" <= @last\w*\) OR \(\w+\.""Month"" = @month\w* AND")
            .And.Contain("@month='7'").And.Contain("@first='1'").And.Contain("@last='13'")
            .And.MatchRegex("@month\\d+='6'").And.MatchRegex("@first\\d+='13'").And.MatchRegex("@last\\d+='30'")
            .And.Contain("@lastYear='2026'");
    }

    [Theory]
    [InlineData(2027, "29")]
    [InlineData(2028, "28")]
    public void Query_WindowOverTheTwentyEighthOfFebruary_ReachesTheTwentyNinthOnlyWithoutALeapDay(int year, string last)
    {
        var sql = WindowSql(new DayWindow(new LocalDate(year, 2, 20), new LocalDate(year, 2, 28).PlusDays(1)));

        sql.Should().Contain("@month='2'").And.Contain("@first='20'").And.Contain($"@last='{last}'");
    }

    [Fact]
    public void Query_WindowAcrossTheNewYear_TestsTheYearBeforeItsLastDay()
    {
        var sql = WindowSql(new DayWindow(new LocalDate(2025, 12, 20), new LocalDate(2026, 1, 5)));

        sql.Should().Contain("@lastYear='2026'");
    }
}
