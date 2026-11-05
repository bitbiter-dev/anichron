using Anichron.API.Endpoints;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class ProxyKindsTests
{
    [Theory]
    [InlineData("thumbnail", ProxyType.Thumbnail, "image/jpeg")]
    [InlineData("preview", ProxyType.FullPreview, "image/jpeg")]
    [InlineData("video", ProxyType.WebVideo, "video/mp4")]
    public void FromSlug_PublishedSlug_GivesItsTypeAndContentType(string slug, ProxyType type, string contentType)
        => ProxyKinds.FromSlug(slug).Should().Be(new ProxyKind(type, slug, contentType));

    [Theory]
    [InlineData("blurhash")]
    [InlineData("original")]
    [InlineData("Thumbnail")]
    [InlineData("")]
    public void FromSlug_UnknownSlug_IsNull(string slug)
        => ProxyKinds.FromSlug(slug).Should().BeNull();

    [Theory]
    [InlineData(ProxyType.Thumbnail, "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/thumbnail?v=0123456789abcdef")]
    [InlineData(ProxyType.FullPreview, "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/preview?v=0123456789abcdef")]
    [InlineData(ProxyType.WebVideo, "proxies/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b/video?v=0123456789abcdef")]
    public void UrlOf_UsesTheTypesSlugAndTheContentVersion(ProxyType type, string url)
        => ProxyKinds.UrlOf(Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b"), type, "0123456789abcdef").Should().Be(url);

    [Fact]
    public void UrlOf_UndefinedType_FailsLoudly()
    {
        var act = () => ProxyKinds.UrlOf(Guid.NewGuid(), (ProxyType)99, "0123456789abcdef");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EveryProxyType_HasExactlyOneKind()
        => ProxyKinds.All.Select(k => k.Type).Should().BeEquivalentTo(Enum.GetValues<ProxyType>());
}
