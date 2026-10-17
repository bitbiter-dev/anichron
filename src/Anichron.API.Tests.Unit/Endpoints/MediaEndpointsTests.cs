using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Infrastructure.FileSystem;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class MediaEndpointsTests
{
    private const string UserHeader = "X-Test-User";
    private const string ContentHash = "0123456789abcdef";
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AssetId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private WebApplication? app;

        public MockFileSystem FileSystem { get; } = new();
        public IMediaAssetReadRepository Assets { get; } = Substitute.For<IMediaAssetReadRepository>();

        public void HasProxy(ProxyType type, string proxyPath, byte[]? bytes)
        {
            Assets.FindVisibleProxyAsync(AssetId, UserId, type, Arg.Any<CancellationToken>())
                .Returns(new VisibleProxy(proxyPath, ContentHash));
            if (bytes is not null)
                FileSystem.AddFile($"/data/proxies/{proxyPath}", new MockFileData(bytes));
        }

        public async Task<HttpClient> StartAsync()
        {
            var builder = ApiTestHost.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", null);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(Assets);
            builder.Services.AddSingleton<IFileSystem>(new ReadOnlyFileSystem(FileSystem));
            app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGroup(ApiPaths.Base).MapMediaEndpoints();
            await app.StartAsync(TestContext.Current.CancellationToken);
            return app.GetTestClient();
        }

        public async ValueTask DisposeAsync()
        {
            if (app is not null)
                await app.DisposeAsync();
        }
    }

    private static HttpRequestMessage Get(string type, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/assets/{AssetId}/proxies/{type}");
        request.Headers.Add(UserHeader, (userId ?? UserId).ToString());
        return request;
    }

    [Theory]
    [InlineData("thumbnail", ProxyType.Thumbnail, "image/jpeg")]
    [InlineData("preview", ProxyType.FullPreview, "image/jpeg")]
    [InlineData("video", ProxyType.WebVideo, "video/mp4")]
    public async Task GetProxy_OwnAssetWithThatProxy_StreamsTheFileWithItsContentTypeAsync(
        string type, ProxyType proxyType, string contentType)
    {
        await using var fixture = new TestFixture();
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        fixture.HasProxy(proxyType, $"01/{type}.bin", bytes);
        using var client = await fixture.StartAsync();

        using var response = await client.SendAsync(Get(type), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(bytes);
    }

    [Fact]
    public async Task GetProxy_OwnAsset_SendsAStrongETagAndPrivateImmutableCachingAsync()
    {
        await using var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        using var client = await fixture.StartAsync();

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
        await using var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        using var client = await fixture.StartAsync();
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
        await using var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.WebVideo, "01/video.mp4", [10, 11, 12, 13, 14, 15, 16, 17]);
        using var client = await fixture.StartAsync();
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
        await using var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1]);
        using var client = await fixture.StartAsync();

        using var _ = await client.SendAsync(Get("thumbnail"), TestContext.Current.CancellationToken);

        await fixture.Assets.Received(1)
            .FindVisibleProxyAsync(AssetId, UserId, ProxyType.Thumbnail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProxy_NotVisibleToTheCaller_ReturnsNotFoundAsync()
    {
        await using var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1]);
        using var client = await fixture.StartAsync();

        using var response = await client.SendAsync(Get("thumbnail", Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("blurhash")]
    [InlineData("original")]
    [InlineData("Thumbnail")]
    public async Task GetProxy_UnknownType_ReturnsNotFoundWithoutALookupAsync(string type)
    {
        await using var fixture = new TestFixture();
        using var client = await fixture.StartAsync();

        using var response = await client.SendAsync(Get(type), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await fixture.Assets.DidNotReceive().FindVisibleProxyAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ProxyType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProxy_RowButNoFileOnDisk_ReturnsNotFoundAsync()
    {
        await using var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", bytes: null);
        using var client = await fixture.StartAsync();

        using var response = await client.SendAsync(Get("thumbnail"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("../originals/mira/private.jpg")]
    [InlineData("/data/originals/mira/private.jpg")]
    [InlineData("01/../../proxies2/x.jpg")]
    public async Task GetProxy_StoredPathEscapingTheProxyRoot_ReturnsNotFoundAsync(string proxyPath)
    {
        await using var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/data/originals/mira/private.jpg", new MockFileData([9, 9, 9]));
        fixture.FileSystem.AddFile("/data/proxies2/x.jpg", new MockFileData([9, 9, 9]));
        fixture.HasProxy(ProxyType.Thumbnail, proxyPath, bytes: null);
        using var client = await fixture.StartAsync();

        using var response = await client.SendAsync(Get("thumbnail"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProxy_Anonymous_ReturnsUnauthorizedAsync()
    {
        await using var fixture = new TestFixture();
        using var client = await fixture.StartAsync();

        using var response = await client.GetAsync(
            $"/api/v1/assets/{AssetId}/proxies/thumbnail", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
