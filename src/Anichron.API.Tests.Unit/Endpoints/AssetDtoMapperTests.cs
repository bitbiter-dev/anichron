using Anichron.API.Endpoints;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AssetDtoMapperTests
{
    private static readonly Guid AssetId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid MovId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-ffffffffffff");

    private static AssetView Photo => new(
        AssetId, MediaType.Image, 2019, 7, 14, new LocalDateTime(2019, 7, 14, 9, 30),
        Width: 4032, Height: 3024, Orientation: ExifOrientation.Upright, DurationInSeconds: 3, Blurhash: null,
        HasThumbnail: false, HasPreview: false, HasVideo: false,
        PairedMovId: null, PairedMovHasVideo: false,
        IsStarred: false, IsLiked: false);

    [Fact]
    public void Photo_MapsItsFieldsAndProxyUrls()
    {
        var view = Photo with { HasThumbnail = true, HasPreview = true, Blurhash = "LEHV6nWB2yk8" };

        AssetDtoMapper.ToDto(view).Should().BeEquivalentTo(new AssetDto(
            AssetId,
            "Image",
            2019,
            7,
            14,
            4032,
            3024,
            null,
            new AssetProxyUrls(
                "assets/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/proxies/thumbnail",
                "assets/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/proxies/preview",
                null),
            "LEHV6nWB2yk8",
            null,
            new AssetInteractionDto(false, false, false, 1.0)));
    }

    [Fact]
    public void Video_ReportsItsDurationAndVideoProxy()
    {
        var dto = AssetDtoMapper.ToDto(Photo with { MediaType = MediaType.Video, HasThumbnail = true, HasVideo = true });

        dto.MediaType.Should().Be("Video");
        dto.DurationSeconds.Should().Be(3);
        dto.Proxies.Should().Be(new AssetProxyUrls(
            "assets/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/proxies/thumbnail",
            null,
            "assets/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/proxies/video"));
    }

    [Fact]
    public void LivePhoto_PointsAtThePairedMovsVideoProxy()
    {
        var dto = AssetDtoMapper.ToDto(Photo with
        {
            MediaType = MediaType.LivePhoto,
            HasThumbnail = true,
            PairedMovId = MovId,
            PairedMovHasVideo = true,
        });

        dto.MediaType.Should().Be("LivePhoto");
        dto.DurationSeconds.Should().BeNull();
        dto.LivePhotoVideoUrl.Should().Be("assets/0199a1b2-c3d4-7e5f-8a9b-ffffffffffff/proxies/video");
        dto.Proxies.Video.Should().BeNull();
    }

    [Fact]
    public void LivePhoto_WhosePairedMovHasNoVideoProxy_HasNoLivePhotoUrl()
    {
        var dto = AssetDtoMapper.ToDto(Photo with { MediaType = MediaType.LivePhoto, PairedMovId = MovId });

        dto.LivePhotoVideoUrl.Should().BeNull();
    }

    [Fact]
    public void MissingOptionalProxiesAndMetadata_MapToNulls()
    {
        var dto = AssetDtoMapper.ToDto(Photo with
        {
            MediaType = MediaType.Video,
            Width = null,
            Height = null,
            Orientation = null,
            DurationInSeconds = null,
        });

        dto.Proxies.Should().Be(new AssetProxyUrls(null, null, null));
        dto.Blurhash.Should().BeNull();
        dto.LivePhotoVideoUrl.Should().BeNull();
        dto.Width.Should().BeNull();
        dto.Height.Should().BeNull();
        dto.DurationSeconds.Should().BeNull();
    }

    [Theory]
    [InlineData(4, 4032, 3024)]
    [InlineData(5, 3024, 4032)]
    [InlineData(6, 3024, 4032)]
    [InlineData(8, 3024, 4032)]
    public void Dimensions_AreReportedUpright(int orientation, int width, int height)
    {
        var dto = AssetDtoMapper.ToDto(Photo with { Orientation = orientation });

        dto.Width.Should().Be(width);
        dto.Height.Should().Be(height);
    }

    [Fact]
    public void NoInteraction_IsAllFalseWithDefaultWeight()
    {
        AssetDtoMapper.ToDto(Photo).Interaction
            .Should().Be(new AssetInteractionDto(false, false, false, 1.0));
    }

    [Fact]
    public void StarredAsset_IsReportedStarred()
    {
        AssetDtoMapper.ToDto(Photo with { IsStarred = true }).Interaction
            .Should().Be(new AssetInteractionDto(true, false, false, 1.0));
    }

    [Fact]
    public void LikedAsset_WeighsDouble()
    {
        AssetDtoMapper.ToDto(Photo with { IsLiked = true }).Interaction
            .Should().Be(new AssetInteractionDto(false, true, false, 2.0));
    }
}
