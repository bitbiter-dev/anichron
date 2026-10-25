using Anichron.API.Endpoints;
using Anichron.Core.Data.Repository;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class FlashbackYearsTests
{
    private static readonly LocalDate Day = new(2026, 7, 14);
    private static readonly Guid BurstA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000a");
    private static readonly Guid BurstB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private static AssetView View(string id, LocalDateTime captured, bool starred = false, bool liked = false, Guid? burst = null)
        => AssetViews.Image(Guid.Parse(id), captured) with { IsStarred = starred, IsLiked = liked, BurstId = burst };

    private static IReadOnlyList<FlashbackYearDto> Group(params AssetView[] views)
        => FlashbackYears.Group(views, Day);

    [Fact]
    public void Group_NoAssets_HasNoYears()
        => Group().Should().BeEmpty();

    [Fact]
    public void Group_OrdersYearsNewestFirstAndOmitsYearsWithoutAssets()
    {
        var years = Group(
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2015, 7, 14, 9, 0)),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2021, 7, 14, 9, 0)),
            View("00000000-0000-0000-0000-000000000003", new LocalDateTime(2015, 7, 14, 10, 0)));

        years.Select(y => y.Year).Should().Equal(2021, 2015);
        years[1].Assets.Should().HaveCount(2);
    }

    [Fact]
    public void Group_WithinAYear_PutsStarredFirstThenCaptureOrder()
    {
        var years = Group(
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 14, 18, 0)),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 14, 20, 0), starred: true),
            View("00000000-0000-0000-0000-000000000003", new LocalDateTime(2019, 7, 14, 8, 0)),
            View("00000000-0000-0000-0000-000000000004", new LocalDateTime(2019, 7, 14, 12, 0), starred: true));

        years.Should().ContainSingle().Which.Assets.Select(a => a.Id.ToString()[^1..])
            .Should().Equal("4", "2", "3", "1");
    }

    [Fact]
    public void Group_SameCaptureTime_IsOrderedById()
    {
        var years = Group(
            View("00000000-0000-0000-0000-00000000000b", new LocalDateTime(2019, 7, 14, 9, 0)),
            View("00000000-0000-0000-0000-00000000000a", new LocalDateTime(2019, 7, 14, 9, 0)));

        years.Should().ContainSingle().Which.Assets.Select(a => a.Id.ToString()[^1..]).Should().Equal("a", "b");
    }

    [Fact]
    public void Group_MapsEachAssetToItsDtoWithoutABurst()
    {
        var view = View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 14, 9, 0), starred: true);

        Group(view).Should().ContainSingle().Which.Assets
            .Should().Equal(AssetDtoMapper.ToDto(view, null));
    }

    // ==========================================================================
    // Bursts (#299)
    // ==========================================================================

    [Fact]
    public void Group_Burst_IsOneItemShowingItsEarliestMemberWithTheMemberCount()
    {
        var cover = View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 14, 9, 0, 0), burst: BurstA);
        var years = Group(
            View("00000000-0000-0000-0000-000000000003", new LocalDateTime(2019, 7, 14, 9, 0, 2), burst: BurstA),
            cover,
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 14, 9, 0, 1), burst: BurstA));

        years.Should().ContainSingle().Which.Assets.Should().Equal(
            AssetDtoMapper.ToDto(cover, new AssetBurstDto(BurstA, IsCover: true, Count: 3)));
    }

    [Fact]
    public void Group_BurstMembersCapturedTogether_CoverIsTheLowestId()
    {
        var years = Group(
            View("00000000-0000-0000-0000-00000000000b", new LocalDateTime(2019, 7, 14, 9, 0), burst: BurstA),
            View("00000000-0000-0000-0000-00000000000a", new LocalDateTime(2019, 7, 14, 9, 0), burst: BurstA));

        years.Should().ContainSingle().Which.Assets.Should().ContainSingle().Which.Id.ToString()[^1..].Should().Be("a");
    }

    [Fact]
    public void Group_TwoBurstsAndASinglePhoto_AreThreeItems()
    {
        var years = Group(
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 14, 9, 0), burst: BurstA),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 14, 9, 1), burst: BurstA),
            View("00000000-0000-0000-0000-000000000003", new LocalDateTime(2019, 7, 14, 10, 0), burst: BurstB),
            View("00000000-0000-0000-0000-000000000004", new LocalDateTime(2019, 7, 14, 10, 1), burst: BurstB),
            View("00000000-0000-0000-0000-000000000005", new LocalDateTime(2019, 7, 14, 11, 0)));

        years.Should().ContainSingle().Which.Assets.Select(a => (a.Id.ToString()[^1..], a.Burst))
            .Should().Equal(
                ("1", new AssetBurstDto(BurstA, true, 2)),
                ("3", new AssetBurstDto(BurstB, true, 2)),
                ("5", null));
    }

    [Fact]
    public void Group_BurstWithAStarredAndALikedMember_IsStarredAndLikedAndSortsFirst()
    {
        var years = Group(
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 14, 8, 0)),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 14, 9, 0), burst: BurstA),
            View("00000000-0000-0000-0000-000000000003", new LocalDateTime(2019, 7, 14, 9, 1), starred: true, burst: BurstA),
            View("00000000-0000-0000-0000-000000000004", new LocalDateTime(2019, 7, 14, 9, 2), liked: true, burst: BurstA));

        var assets = years.Should().ContainSingle().Which.Assets;
        assets.Select(a => a.Id.ToString()[^1..]).Should().Equal("2", "1");
        assets[0].Interaction.Should().Be(new AssetInteractionDto(true, true, false, 2.0));
    }

    [Fact]
    public void Group_BurstWithoutStarsOrLikes_IsNeither()
    {
        var years = Group(
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 14, 9, 0), burst: BurstA),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 14, 9, 1), burst: BurstA));

        years.Should().ContainSingle().Which.Assets.Should().ContainSingle().Which.Interaction
            .Should().Be(new AssetInteractionDto(false, false, false, 1.0));
    }

    [Fact]
    public void Group_BurstAcrossMidnightIntoTheDay_BelongsToTheDayBefore()
    {
        var years = Group(
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 13, 23, 59), burst: BurstA),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 14, 0, 1), burst: BurstA));

        years.Should().BeEmpty();
    }

    [Fact]
    public void Group_BurstAcrossMidnightOutOfTheDay_IsShownWithEveryMemberCounted()
    {
        var years = Group(
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 14, 23, 59), burst: BurstA),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 15, 0, 1), burst: BurstA));

        years.Should().ContainSingle().Which.Assets.Should().ContainSingle().Which.Burst
            .Should().Be(new AssetBurstDto(BurstA, true, 2));
    }

    [Fact]
    public void Group_BurstWhoseMembersBeforeMidnightAreHidden_IsShownOnTheNextDayWithOnlyTheVisibleMember()
    {
        var years = FlashbackYears.Group(
            [View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 15, 0, 1), burst: BurstA)],
            new LocalDate(2026, 7, 15));

        years.Should().ContainSingle().Which.Assets.Should().ContainSingle().Which.Burst
            .Should().Be(new AssetBurstDto(BurstA, true, 1));
    }

    [Fact]
    public void Group_BurstStartingOnTheSameDayOfAnEarlierMonth_IsNotShown()
    {
        var years = Group(
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 6, 14, 9, 0), burst: BurstA),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 14, 9, 0), burst: BurstA));

        years.Should().BeEmpty();
    }
}
