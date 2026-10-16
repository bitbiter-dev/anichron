using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.Core.Data.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class RouteTableTests
{
    private sealed record Route(string Method, string Pattern, bool Anonymous, string[] Policies, bool Authenticated, string? RateLimit, string[] Tags);

    private static IReadOnlyList<Route> MapRoutes()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton(Substitute.For<IAuthService>());
        builder.Services.AddSingleton(Substitute.For<IAuthResponseMapper>());
        builder.Services.AddSingleton(Substitute.For<IAdminResetService>());
        builder.Services.AddSingleton(Substitute.For<IAdminUserService>());
        builder.Services.AddSingleton(Substitute.For<IAdminStorageConfigService>());
        builder.Services.AddSingleton(Substitute.For<IUserRepository>());
        builder.Services.AddSingleton(Substitute.For<IMediaAssetReadRepository>());
        var app = builder.Build();

        app.MapApiEndpoints();

        return [.. ((IEndpointRouteBuilder)app).DataSources
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

    private static IReadOnlyList<Route> Routes => MapRoutes();

    private static Route Find(string method, string pattern)
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

    [Fact]
    public void ProxyRoute_RequiresAnAuthenticatedUserButNotAdmin()
    {
        var route = Find("GET", "/api/v1/assets/{assetId:guid}/proxies/{type}");

        route.Anonymous.Should().BeFalse();
        route.Authenticated.Should().BeTrue();
        route.Policies.Should().NotContain(AuthPolicies.Admin);
    }

    [Fact]
    public void AssetRoutes_AreTaggedMedia()
        => Routes.Where(r => r.Pattern.StartsWith("/api/v1/assets", StringComparison.Ordinal))
            .Should().NotBeEmpty().And.OnlyContain(r => r.Tags.SequenceEqual(new[] { "Media" }));

    [Fact]
    public void Healthz_IsAnonymous()
        => Find("GET", "/api/v1/healthz").Anonymous.Should().BeTrue();

    [Fact]
    public void EveryMappedRoute_IsCoveredByTheTestsAbove()
        => Routes.Should().HaveCount(20);
}
