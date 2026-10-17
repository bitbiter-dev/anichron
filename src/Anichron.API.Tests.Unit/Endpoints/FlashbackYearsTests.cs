using Anichron.API.Endpoints;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class FlashbackYearsTests
{
    private static AssetView View(string id, LocalDateTime captured, bool starred = false) => new(
        Guid.Parse(id), MediaType.Image, captured.Year, captured.Month, captured.Day, captured,
        Width: null, Height: null, Orientation: null, DurationInSeconds: null, Blurhash: null,
        HasThumbnail: false, HasPreview: false, HasVideo: false,
        PairedMovId: null, PairedMovHasVideo: false,
        IsStarred: starred, IsLiked: false);

    [Fact]
    public void Group_NoAssets_HasNoYears()
        => FlashbackYears.Group([]).Should().BeEmpty();

    [Fact]
    public void Group_OrdersYearsNewestFirstAndOmitsYearsWithoutAssets()
    {
        var years = FlashbackYears.Group(
        [
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2015, 7, 14, 9, 0)),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2021, 7, 14, 9, 0)),
            View("00000000-0000-0000-0000-000000000003", new LocalDateTime(2015, 7, 14, 10, 0)),
        ]);

        years.Select(y => y.Year).Should().Equal(2021, 2015);
        years[1].Assets.Should().HaveCount(2);
    }

    [Fact]
    public void Group_WithinAYear_PutsStarredFirstThenCaptureOrder()
    {
        var years = FlashbackYears.Group(
        [
            View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 14, 18, 0)),
            View("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 14, 20, 0), starred: true),
            View("00000000-0000-0000-0000-000000000003", new LocalDateTime(2019, 7, 14, 8, 0)),
            View("00000000-0000-0000-0000-000000000004", new LocalDateTime(2019, 7, 14, 12, 0), starred: true),
        ]);

        years.Should().ContainSingle().Which.Assets.Select(a => a.Id.ToString()[^1..])
            .Should().Equal("4", "2", "3", "1");
    }

    [Fact]
    public void Group_SameCaptureTime_IsOrderedById()
    {
        var years = FlashbackYears.Group(
        [
            View("00000000-0000-0000-0000-00000000000b", new LocalDateTime(2019, 7, 14, 9, 0)),
            View("00000000-0000-0000-0000-00000000000a", new LocalDateTime(2019, 7, 14, 9, 0)),
        ]);

        years.Should().ContainSingle().Which.Assets.Select(a => a.Id.ToString()[^1..]).Should().Equal("a", "b");
    }

    [Fact]
    public void Group_MapsEachAssetToItsDto()
    {
        var view = View("00000000-0000-0000-0000-000000000001", new LocalDateTime(2019, 7, 14, 9, 0), starred: true);

        FlashbackYears.Group([view]).Should().ContainSingle().Which.Assets
            .Should().Equal(AssetDtoMapper.ToDto(view));
    }
}
