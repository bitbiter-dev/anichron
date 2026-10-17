using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class MediaEndpointsTests
{
    private const string ContentHash = "0123456789abcdef";
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AssetId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private sealed class FakeProxyStore : IProxyStore
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public bool RootExists() => true;

        public Stream? OpenRead(string proxyPath)
            => Files.TryGetValue(proxyPath, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
    }

    private sealed class TestFixture
    {
        public FakeProxyStore Proxies { get; } = new();
        public IMediaAssetReadRepository Assets { get; } = Substitute.For<IMediaAssetReadRepository>();

        public void HasProxy(ProxyType type, string proxyPath, byte[]? bytes)
        {
            Assets.FindVisibleProxyAsync(AssetId, UserId, type, Arg.Any<CancellationToken>())
                .Returns(new VisibleProxy(proxyPath, ContentHash));
            if (bytes is not null)
                Proxies.Files[proxyPath] = bytes;
        }

        public Task<AuthenticatedTestApp> StartAsync()
            => AuthenticatedTestApp.StartAsync(services =>
            {
                services.AddSingleton(Assets);
                services.AddSingleton<IProxyStore>(Proxies);
            });
    }

    private static HttpRequestMessage Get(string type, Guid? userId = null)
        => AuthenticatedTestApp.Get($"/api/v1/assets/{AssetId}/proxies/{type}", userId ?? UserId);

    [Theory]
    [InlineData("thumbnail", ProxyType.Thumbnail, "image/jpeg")]
    [InlineData("preview", ProxyType.FullPreview, "image/jpeg")]
    [InlineData("video", ProxyType.WebVideo, "video/mp4")]
    public async Task GetProxy_OwnAssetWithThatProxy_StreamsTheFileWithItsContentTypeAsync(
        string type, ProxyType proxyType, string contentType)
    {
        var fixture = new TestFixture();
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        fixture.HasProxy(proxyType, $"01/{type}.bin", bytes);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get(type), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(bytes);
    }

    [Fact]
    public async Task GetProxy_OwnAsset_SendsAStrongETagAndPrivateImmutableCachingAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get("thumbnail"), TestContext.Current.CancellationToken);

        response.Headers.ETag.Should().Be(new EntityTagHeaderValue($"\"{ContentHash}-{ProxyType.Thumbnail}\""));
        var cacheControl = response.Headers.CacheControl!;
        cacheControl.Private.Should().BeTrue();
        cacheControl.Public.Should().BeFalse();
        cacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        cacheControl.Extensions.Should().ContainSingle(e => e.Name == "immutable");
    }

    [Fact]
    public async Task GetProxy_MatchingIfNoneMatch_ReturnsNotModifiedAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;
        var request = Get("thumbnail");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{ContentHash}-{ProxyType.Thumbnail}\""));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        response.Headers.ETag.Should().Be(new EntityTagHeaderValue($"\"{ContentHash}-{ProxyType.Thumbnail}\""));
        response.Headers.CacheControl!.Private.Should().BeTrue();
    }

    [Fact]
    public async Task GetProxy_VideoRangeRequest_ReturnsPartialContentAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.WebVideo, "01/video.mp4", [10, 11, 12, 13, 14, 15, 16, 17]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;
        var request = Get("video");
        request.Headers.Range = new RangeHeaderValue(2, 4);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(12, 13, 14);
        response.Headers.ETag.Should().Be(new EntityTagHeaderValue($"\"{ContentHash}-{ProxyType.WebVideo}\""));
        response.Headers.CacheControl!.Private.Should().BeTrue();
    }

    [Fact]
    public async Task GetProxy_LooksUpTheCallerFromTheirClaimsAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var _ = await client.SendAsync(Get("thumbnail"), TestContext.Current.CancellationToken);

        await fixture.Assets.Received(1)
            .FindVisibleProxyAsync(AssetId, UserId, ProxyType.Thumbnail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProxy_NotVisibleToTheCaller_ReturnsNotFoundAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get("thumbnail", Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("blurhash")]
    [InlineData("original")]
    [InlineData("Thumbnail")]
    public async Task GetProxy_UnknownType_ReturnsNotFoundWithoutALookupAsync(string type)
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get(type), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await fixture.Assets.DidNotReceive().FindVisibleProxyAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ProxyType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProxy_StoreHasNoFileForTheRow_ReturnsNotFoundAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", bytes: null);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get("thumbnail"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProxy_Anonymous_ReturnsUnauthorizedAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.GetAsync(
            $"/api/v1/assets/{AssetId}/proxies/thumbnail", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
