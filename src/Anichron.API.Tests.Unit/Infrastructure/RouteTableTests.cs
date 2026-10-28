using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class RouteTableTests(RouteTableTests.RouteTable table) : IClassFixture<RouteTableTests.RouteTable>
{
    internal sealed record Route(string Method, string Pattern, bool Anonymous, string[] Policies, bool Authenticated, string? RateLimit, string[] Tags);

    public sealed class RouteTable
    {
        internal IReadOnlyList<Route> Routes { get; }

        public RouteTable()
        {
            var builder = ApiTestHost.CreateBuilder();
            using var app = builder.Build();

            app.MapApiEndpoints();

            Routes = [.. ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                    .Select(method => new Route(
                        method,
                        "/" + endpoint.RoutePattern.RawText!.Trim('/'),
                        endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                        [.. endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>()],
                        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(),
                        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName,
                        [.. endpoint.Metadata.GetOrderedMetadata<ITagsMetadata>().SelectMany(t => t.Tags)])))];
        }
    }

    private IReadOnlyList<Route> Routes => table.Routes;

    private Route Find(string method, string pattern)
        => Routes.Should().ContainSingle(r => r.Method == method && r.Pattern == pattern,
            $"{method} {pattern} must be mapped exactly once").Which;

    [Theory]
    [InlineData("POST", "/api/v1/auth/register", "auth-sensitive")]
    [InlineData("POST", "/api/v1/auth/login", "auth-sensitive")]
    [InlineData("POST", "/api/v1/auth/login/mobile", "auth-sensitive")]
    [InlineData("POST", "/api/v1/auth/refresh", "auth-refresh")]
    [InlineData("POST", "/api/v1/auth/password-reset/request", "auth-sensitive")]
    [InlineData("POST", "/api/v1/auth/password-reset/confirm", "auth-sensitive")]
    public void AnonymousAuthRoutes_AreRateLimited(string method, string pattern, string rateLimit)
    {
        var route = Find(method, pattern);

        route.Anonymous.Should().BeTrue();
        route.RateLimit.Should().Be(rateLimit);
    }

    [Fact]
    public void Logout_RequiresAnAuthenticatedUser()
    {
        var route = Find("POST", "/api/v1/auth/logout");

        route.Anonymous.Should().BeFalse();
        route.Authenticated.Should().BeTrue();
    }

    [Theory]
    [InlineData("GET", "/api/v1/users/me", null)]
    [InlineData("POST", "/api/v1/users/me/password", "auth-sensitive")]
    public void SelfServiceUserRoutes_RequireAuthenticationButNotAdmin(string method, string pattern, string? rateLimit)
    {
        var route = Find(method, pattern);

        route.Authenticated.Should().BeTrue();
        route.Policies.Should().NotContain(AuthPolicies.Admin);
        route.RateLimit.Should().Be(rateLimit);
    }

    [Theory]
    [InlineData("POST", "/api/v1/users", "auth-sensitive")]
    [InlineData("GET", "/api/v1/users", null)]
    [InlineData("GET", "/api/v1/users/{userId:guid}", null)]
    [InlineData("PATCH", "/api/v1/users/{userId:guid}", "auth-sensitive")]
    [InlineData("DELETE", "/api/v1/users/{userId:guid}", "auth-sensitive")]
    [InlineData("POST", "/api/v1/users/{userId:guid}/password-reset", "auth-sensitive")]
    [InlineData("GET", "/api/v1/users/{userId:guid}/storage-configs", null)]
    [InlineData("POST", "/api/v1/users/{userId:guid}/storage-configs", "auth-sensitive")]
    [InlineData("DELETE", "/api/v1/users/{userId:guid}/storage-configs/{configId:guid}", "auth-sensitive")]
    public void AdminRoutes_RequireTheAdminPolicy(string method, string pattern, string? rateLimit)
    {
        var route = Find(method, pattern);

        route.Anonymous.Should().BeFalse();
        route.Policies.Should().Contain(AuthPolicies.Admin);
        route.RateLimit.Should().Be(rateLimit);
    }

    [Fact]
    public void AuthRoutes_AreTaggedAuth()
        => Routes.Where(r => r.Pattern.StartsWith("/api/v1/auth/", StringComparison.Ordinal))
            .Should().OnlyContain(r => r.Tags.SequenceEqual(new[] { "Auth" }));

    [Fact]
    public void UserRoutes_AreTaggedUsers()
        => Routes.Where(r => r.Pattern.StartsWith("/api/v1/users", StringComparison.Ordinal))
            .Should().OnlyContain(r => r.Tags.SequenceEqual(new[] { "Users" }));

    [Theory]
    [InlineData("GET", "/api/v1/assets/{assetId:guid}/proxies/{type}")]
    [InlineData("PATCH", "/api/v1/assets/{assetId:guid}/interaction")]
    public void AssetRoutes_RequireAnAuthenticatedUserButNotAdmin(string method, string pattern)
    {
        var route = Find(method, pattern);

        route.Anonymous.Should().BeFalse();
        route.Authenticated.Should().BeTrue();
        route.Policies.Should().NotContain(AuthPolicies.Admin);
    }

    [Fact]
    public void AssetRoutes_AreTaggedMedia()
        => Routes.Where(r => r.Pattern.StartsWith("/api/v1/assets", StringComparison.Ordinal))
            .Should().NotBeEmpty().And.OnlyContain(r => r.Tags.SequenceEqual(new[] { "Media" }));

    [Fact]
    public void OnThisDayRoute_RequiresAnAuthenticatedUserButNotAdmin()
    {
        var route = Find("GET", "/api/v1/flashbacks/on-this-day");

        route.Anonymous.Should().BeFalse();
        route.Authenticated.Should().BeTrue();
        route.Policies.Should().NotContain(AuthPolicies.Admin);
    }

    [Fact]
    public void FlashbackRoutes_AreTaggedFlashbacks()
        => Routes.Where(r => r.Pattern.StartsWith("/api/v1/flashbacks", StringComparison.Ordinal))
            .Should().NotBeEmpty().And.OnlyContain(r => r.Tags.SequenceEqual(new[] { "Flashbacks" }));

    [Fact]
    public void BurstMembersRoute_RequiresAnAuthenticatedUserButNotAdminAndIsTaggedBursts()
    {
        var route = Find("GET", "/api/v1/bursts/{burstId:guid}/assets");

        route.Anonymous.Should().BeFalse();
        route.Authenticated.Should().BeTrue();
        route.Policies.Should().NotContain(AuthPolicies.Admin);
        route.Tags.Should().Equal("Bursts");
    }

    [Theory]
    [InlineData("/api/v1/admin/work-items")]
    [InlineData("/api/v1/admin/work-items/{workItemId:guid}")]
    [InlineData("/api/v1/admin/work-items/events")]
    public void WorkItemRoutes_RequireTheAdminPolicyWithoutRateLimit(string pattern)
        => AssertAdminWithoutRateLimit(Find("GET", pattern));

    [Fact]
    public void CancelWorkItemRoute_RequiresTheAdminPolicyWithoutRateLimit()
        => AssertAdminWithoutRateLimit(Find("POST", "/api/v1/admin/work-items/{workItemId:guid}/cancel"));

    private static void AssertAdminWithoutRateLimit(Route route)
    {

        route.Anonymous.Should().BeFalse();
        route.Policies.Should().Contain(AuthPolicies.Admin);
        route.RateLimit.Should().BeNull();
    }

    [Fact]
    public void WorkItemRoutes_AreTaggedWorkItems()
        => Routes.Where(r => r.Pattern.StartsWith("/api/v1/admin/work-items", StringComparison.Ordinal))
            .Should().HaveCount(4).And.OnlyContain(r => r.Tags.SequenceEqual(new[] { "Work items" }));

    [Theory]
    [InlineData("POST", "/api/v1/admin/proxy-sweeps")]
    [InlineData("GET", "/api/v1/admin/proxy-sweeps/{sweepId:guid}/orphans")]
    [InlineData("POST", "/api/v1/admin/proxy-sweeps/{sweepId:guid}/deletions")]
    public void ProxySweepRoutes_RequireTheAdminPolicyWithoutRateLimit(string method, string pattern)
        => AssertAdminWithoutRateLimit(Find(method, pattern));

    [Fact]
    public void ProxySweepRoutes_AreTaggedProxySweeps()
        => Routes.Where(r => r.Pattern.StartsWith("/api/v1/admin/proxy-sweeps", StringComparison.Ordinal))
            .Should().HaveCount(3).And.OnlyContain(r => r.Tags.SequenceEqual(new[] { "Proxy sweeps" }));

    [Fact]
    public void Healthz_IsAnonymous()
        => Find("GET", "/api/v1/healthz").Anonymous.Should().BeTrue();

    [Fact]
    public void EveryMappedRoute_IsCoveredByTheTestsAbove()
        => Routes.Should().HaveCount(30);
}
