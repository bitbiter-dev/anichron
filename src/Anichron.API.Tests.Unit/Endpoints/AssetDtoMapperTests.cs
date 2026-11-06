using Anichron.API.Endpoints;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AssetDtoMapperTests
{
    private static readonly Guid AssetId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid MovId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-ffffffffffff");
    private const string MovHash = "cafebabecafebabe";

    private static AssetView Photo => AssetViews.Image(AssetId, new LocalDateTime(2019, 7, 14, 9, 30)) with
    {
        Width = 4032,
        Height = 3024,
        Orientation = ExifOrientation.Upright,
        DurationInSeconds = 3,
    };

    private static AssetDto ToDto(AssetView view) => AssetDtoMapper.ToDto(view, null);

    [Fact]
    public void Photo_MapsItsFieldsAndProxyUrls()
    {
        var view = Photo with { HasThumbnail = true, HasPreview = true, Blurhash = "LEHV6nWB2yk8" };

        ToDto(view).Should().BeEquivalentTo(new AssetDto(
            AssetId,
            "Image",
            2019,
            7,
            14,
            4032,
            3024,
            null,
            new AssetProxyUrls(
                "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/thumbnail?v=0123456789abcdef",
                "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/preview?v=0123456789abcdef",
                null),
            "LEHV6nWB2yk8",
            null,
            new AssetInteractionDto(false, false, false, 1.0),
            null));
    }

    [Fact]
    public void Burst_IsCarriedOnTheDto()
    {
        var burst = new AssetBurstDto(Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-bbbbbbbbbbbb"), IsCover: true, Count: 3);

        AssetDtoMapper.ToDto(Photo, burst).Burst.Should().Be(burst);
    }

    [Fact]
    public void Video_ReportsItsDurationAndVideoProxy()
    {
        var dto = ToDto(Photo with { MediaType = MediaType.Video, HasThumbnail = true, HasVideo = true });

        dto.MediaType.Should().Be("Video");
        dto.DurationSeconds.Should().Be(3);
        dto.Proxies.Should().Be(new AssetProxyUrls(
            "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/thumbnail?v=0123456789abcdef",
            null,
            "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/video?v=0123456789abcdef"));
    }

    [Fact]
    public void AssetThatTookNewContent_KeepsItsIdButEveryProxyUrlChanges()
    {
        var before = Photo with { MediaType = MediaType.Video, HasThumbnail = true, HasPreview = true, HasVideo = true };

        var after = ToDto(before with { ContentHash = "fedcba9876543210" });

        after.Id.Should().Be(ToDto(before).Id);
        after.Proxies.Should().Be(new AssetProxyUrls(
            "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/thumbnail?v=fedcba9876543210",
            "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/preview?v=fedcba9876543210",
            "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/video?v=fedcba9876543210"));
    }

    [Fact]
    public void LivePhoto_PointsAtThePairedMovsVideoProxy()
    {
        var dto = ToDto(Photo with
        {
            MediaType = MediaType.LivePhoto,
            HasThumbnail = true,
            PairedMovId = MovId,
            PairedMovContentHash = MovHash,
            PairedMovHasVideo = true,
        });

        dto.MediaType.Should().Be("LivePhoto");
        dto.DurationSeconds.Should().BeNull();
        dto.LivePhotoVideoUrl.Should().Be("proxies/0199a1b2-c3d4-7e5f-8a9b-ffffffffffff/video?v=cafebabecafebabe");
        dto.Proxies.Video.Should().BeNull();
    }

    [Fact]
    public void LivePhoto_WhosePairedMovHasNoVideoProxy_IsPublishedAsAStillImage()
    {
        var dto = ToDto(Photo with { MediaType = MediaType.LivePhoto, PairedMovId = MovId, PairedMovContentHash = MovHash });

        dto.LivePhotoVideoUrl.Should().BeNull();
        dto.MediaType.Should().Be("Image");
    }

    [Fact]
    public void LivePhoto_WithoutAPairedMov_IsPublishedAsAStillImage()
    {
        var dto = ToDto(Photo with { MediaType = MediaType.LivePhoto, PairedMovHasVideo = true });

        dto.LivePhotoVideoUrl.Should().BeNull();
        dto.MediaType.Should().Be("Image");
    }

    [Fact]
    public void MissingOptionalProxiesAndMetadata_MapToNulls()
    {
        var dto = ToDto(Photo with
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
        var dto = ToDto(Photo with { Orientation = orientation });

        dto.Width.Should().Be(width);
        dto.Height.Should().Be(height);
    }

    [Fact]
    public void NoInteraction_IsAllFalseWithDefaultWeight()
    {
        ToDto(Photo).Interaction
            .Should().Be(new AssetInteractionDto(false, false, false, 1.0));
    }

    [Fact]
    public void StarredAsset_IsReportedStarred()
    {
        ToDto(Photo with { IsStarred = true }).Interaction
            .Should().Be(new AssetInteractionDto(true, false, false, 1.0));
    }

    [Fact]
    public void LikedAsset_WeighsDouble()
    {
        ToDto(Photo with { IsLiked = true }).Interaction
            .Should().Be(new AssetInteractionDto(false, true, false, 2.0));
    }

    [Theory]
    [InlineData(true, false, false, 1.0)]
    [InlineData(false, true, false, 2.0)]
    [InlineData(false, false, true, 1.0)]
    public void InteractionState_MapsEachFlagAndWeighsALikeDouble(bool starred, bool liked, bool hidden, double weight)
        => AssetDtoMapper.ToDto(new InteractionState(starred, liked, hidden))
            .Should().Be(new AssetInteractionDto(starred, liked, hidden, weight));

    private static AssetView WithLiveVideo(MediaType type)
        => Photo with { MediaType = type, PairedMovId = MovId, PairedMovContentHash = MovHash, PairedMovHasVideo = true };

    [Theory]
    [InlineData(MediaType.Image, "Image")]
    [InlineData(MediaType.Video, "Video")]
    [InlineData(MediaType.LivePhoto, "LivePhoto")]
    public void MediaType_IsPublishedAsItsContractValue(MediaType type, string published)
        => ToDto(WithLiveVideo(type)).MediaType.Should().Be(published);

    [Fact]
    public void EveryMediaType_HasAPublishedValue()
        => Enum.GetValues<MediaType>().Select(t => ToDto(WithLiveVideo(t)).MediaType)
            .Should().OnlyHaveUniqueItems().And.HaveCount(Enum.GetValues<MediaType>().Length);
}
