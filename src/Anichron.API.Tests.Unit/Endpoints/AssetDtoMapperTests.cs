using Anichron.API.Endpoints;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AssetDtoMapperTests
{
    private static readonly Guid AssetId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid MovId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-ffffffffffff");

    private static MediaAsset Photo(int orientation = ExifOrientation.Upright, params ProxyType[] proxies) => new()
    {
        Id = AssetId,
        MediaType = MediaType.Image,
        Year = 2019,
        Month = 7,
        Day = 14,
        Metadata = new Metadata { Width = 4032, Height = 3024, Orientation = orientation, DurationInSeconds = 3 },
        ProxyFiles = [.. proxies.Select(type => new ProxyFile { ProxyType = type })],
    };

    [Fact]
    public void Photo_MapsItsFieldsAndProxyUrls()
    {
        var asset = Photo(ExifOrientation.Upright, ProxyType.Thumbnail, ProxyType.FullPreview, ProxyType.BlurHash);

        var dto = AssetDtoMapper.ToDto(asset, interaction: null, blurhash: "LEHV6nWB2yk8");

        dto.Should().BeEquivalentTo(new AssetDto(
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
        var asset = Photo(ExifOrientation.Upright, ProxyType.Thumbnail, ProxyType.WebVideo);
        asset.MediaType = MediaType.Video;

        var dto = AssetDtoMapper.ToDto(asset, null, null);

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
        var asset = Photo(ExifOrientation.Upright, ProxyType.Thumbnail);
        asset.MediaType = MediaType.LivePhoto;
        asset.PairedAssetId = MovId;
        asset.PairedAsset = new MediaAsset
        {
            Id = MovId,
            MediaType = MediaType.Video,
            ProxyFiles = [new ProxyFile { ProxyType = ProxyType.WebVideo }],
        };

        var dto = AssetDtoMapper.ToDto(asset, null, null);

        dto.MediaType.Should().Be("LivePhoto");
        dto.DurationSeconds.Should().BeNull();
        dto.LivePhoto.Should().Be("assets/0199a1b2-c3d4-7e5f-8a9b-ffffffffffff/proxies/video");
        dto.Proxies.Video.Should().BeNull();
    }

    [Fact]
    public void LivePhoto_WhosePairedMovHasNoVideoProxy_HasNoLivePhotoUrl()
    {
        var asset = Photo();
        asset.MediaType = MediaType.LivePhoto;
        asset.PairedAssetId = MovId;
        asset.PairedAsset = new MediaAsset
        {
            Id = MovId,
            MediaType = MediaType.Video,
            ProxyFiles = [new ProxyFile { ProxyType = ProxyType.Thumbnail }],
        };

        AssetDtoMapper.ToDto(asset, null, null).LivePhoto.Should().BeNull();
    }

    [Fact]
    public void MissingOptionalProxiesAndMetadata_MapToNulls()
    {
        var asset = new MediaAsset { Id = AssetId, MediaType = MediaType.Video };

        var dto = AssetDtoMapper.ToDto(asset, null, null);

        dto.Proxies.Should().Be(new AssetProxyUrls(null, null, null));
        dto.Blurhash.Should().BeNull();
        dto.LivePhoto.Should().BeNull();
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
        var dto = AssetDtoMapper.ToDto(Photo(orientation), null, null);

        dto.Width.Should().Be(width);
        dto.Height.Should().Be(height);
    }

    [Fact]
    public void NoInteractionRow_IsAllFalseWithDefaultWeight()
    {
        AssetDtoMapper.ToDto(Photo(), null, null).Interaction
            .Should().Be(new AssetInteractionDto(false, false, false, 1.0));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    public void InteractionRow_MapsTheCallersFlags(bool starred, bool liked, bool hidden)
    {
        var interaction = new AssetInteraction { IsStarred = starred, IsLiked = liked, IsHidden = hidden };

        AssetDtoMapper.ToDto(Photo(), interaction, null).Interaction
            .Should().Be(new AssetInteractionDto(starred, liked, hidden, 1.0));
    }

    [Fact]
    public void LikedAsset_WeighsDouble()
    {
        var interaction = new AssetInteraction { IsLiked = true };

        AssetDtoMapper.ToDto(Photo(), interaction, null).Interaction
            .Should().Be(new AssetInteractionDto(false, true, false, 2.0));
    }

    [Fact]
    public void OtherUsersInteractionsLoadedOnTheAsset_AreIgnored()
    {
        var asset = Photo();
        asset.Interactions = [new AssetInteraction { IsStarred = true, IsLiked = true, IsHidden = true }];

        AssetDtoMapper.ToDto(asset, null, null).Interaction
            .Should().Be(new AssetInteractionDto(false, false, false, 1.0));
    }
}
