using Anichron.API.Endpoints;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class ProxyEndpointsTests
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

        public Task<JwtTestApp> StartAsync()
            => JwtTestApp.StartAsync(app => app.MapGroup("/api/v1").MapProxyEndpoints(), services =>
            {
                services.AddSingleton(Assets);
                services.AddSingleton<IProxyStore>(Proxies);
            });
    }

    private static HttpRequestMessage Unauthenticated(string type = "thumbnail", string query = $"?v={ContentHash}")
        => new(HttpMethod.Get, $"/api/v1/proxies/{AssetId}/{type}{query}");

    private static HttpRequestMessage Get(string type, Guid? userId = null, string query = $"?v={ContentHash}")
        => JwtTestApp.WithBearer(Unauthenticated(type, query), JwtTestApp.TokenFor(userId ?? UserId));

    private static string CacheControlOf(HttpResponseMessage response)
        => response.Headers.NonValidated["Cache-Control"].ToString();

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
    public async Task GetProxy_CurrentVersion_SendsAStrongETagAndPrivateImmutableCachingAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get("thumbnail", query: "?v=0123456789abcdef"), TestContext.Current.CancellationToken);

        response.Headers.ETag.Should().Be(new EntityTagHeaderValue("\"0123456789abcdef-Thumbnail\""));
        CacheControlOf(response).Should().Be("private, max-age=31536000, immutable");
    }

    [Theory]
    [InlineData("?v=fedcba9876543210")]
    [InlineData("?v=0123456789ABCDEF")]
    [InlineData("?v=")]
    [InlineData("")]
    public async Task GetProxy_StaleOrMissingVersion_ServesTheCurrentFileForRevalidationAsync(string query)
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get("thumbnail", query: query), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(1, 2, 3);
        response.Headers.ETag.Should().Be(new EntityTagHeaderValue("\"0123456789abcdef-Thumbnail\""));
        CacheControlOf(response).Should().Be("private, no-cache");
    }

    [Fact]
    public async Task GetProxy_StaleVersionWithTheCurrentETag_IsNotModifiedAndStillRevalidatedAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;
        var request = Get("thumbnail", query: "?v=fedcba9876543210");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"0123456789abcdef-Thumbnail\""));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        CacheControlOf(response).Should().Be("private, no-cache");
    }

    [Fact]
    public async Task GetProxy_MatchingIfNoneMatch_ReturnsNotModifiedAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;
        var request = Get("thumbnail");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"0123456789abcdef-Thumbnail\""));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        response.Headers.ETag.Should().Be(new EntityTagHeaderValue("\"0123456789abcdef-Thumbnail\""));
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
        response.Headers.ETag.Should().Be(new EntityTagHeaderValue("\"0123456789abcdef-WebVideo\""));
        response.Headers.CacheControl!.Private.Should().BeTrue();
    }

    [Fact]
    public async Task GetProxy_MismatchedIfMatch_IsAPreconditionFailedProblemAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;
        var request = Get("thumbnail");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"other\""));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await ProblemAssertions.AssertStatusProblemAsync(response, 412);
    }

    [Fact]
    public async Task GetProxy_RangeBeyondTheFile_IsRangeNotSatisfiableWithoutABodyAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.WebVideo, "01/video.mp4", [10, 11, 12]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;
        var request = Get("video");
        request.Headers.Range = new RangeHeaderValue(100, 200);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
        response.Content.Headers.ContentRange!.ToString().Should().Be("bytes */3");
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
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
            $"/api/v1/proxies/{AssetId}/thumbnail?v={ContentHash}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ==========================================================================
    // The media cookie (ADR 0013)
    // ==========================================================================

    [Fact]
    public async Task GetProxy_ByMediaCookie_StreamsTheFileAsync()
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            JwtTestApp.WithMediaCookie(Unauthenticated(), JwtTestApp.TokenFor(UserId)), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(1, 2, 3);
    }

    public static TheoryData<string> RefusedTokens
        => [
            JwtTestApp.TokenFor(UserId, issuedAt: SystemClock.Instance.GetCurrentInstant() - Duration.FromHours(1)),
            JwtTestApp.TokenFor(UserId, secret: "another-secret-that-is-long-enough-for-hmac-sha256-signing"),
            "not-a-token",
        ];

    [Theory]
    [MemberData(nameof(RefusedTokens))]
    public async Task GetProxy_ExpiredOrTamperedMediaCookie_IsUnauthorizedAsync(string token)
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            JwtTestApp.WithMediaCookie(Unauthenticated(), token), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetProxy_UserWhoMustChangePassword_IsAPasswordChangeRequiredProblemAsync(bool byCookie)
    {
        var fixture = new TestFixture();
        fixture.HasProxy(ProxyType.Thumbnail, "01/thumbnail.jpg", [1, 2, 3]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;
        var token = JwtTestApp.TokenFor(UserId, mustChangePassword: true);
        var request = byCookie ? JwtTestApp.WithMediaCookie(Unauthenticated(), token) : JwtTestApp.WithBearer(Unauthenticated(), token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await ProblemAssertions.AssertProblemAsync(
            response, 403, "password_change_required", "Password change required before continuing.");
    }

    [Fact]
    public async Task EveryOtherSignedInRoute_IgnoresTheMediaCookieAsync()
    {
        await using var app = await JwtTestApp.StartAsync(app => app.MapApiEndpoints(), _ => { });
        using var client = app.Client;
        var token = JwtTestApp.TokenFor(UserId);
        var routes = app.SignedInRoutes().Where(route => !route.Path.StartsWith("/api/v1/proxies/", StringComparison.Ordinal)).ToList();

        List<string> served = [];
        foreach (var (method, path) in routes)
        {
            using var response = await client.SendAsync(
                JwtTestApp.WithMediaCookie(new HttpRequestMessage(new HttpMethod(method), path), token), TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                served.Add($"{method} {path}: {(int)response.StatusCode}");
        }

        routes.Should().Contain(("GET", "/api/v1/users/me")).And.Contain(("PATCH", $"/api/v1/assets/{JwtTestApp.RouteValue}/interaction"));
        served.Should().BeEmpty();
    }
}
