using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Security;
using Anichron.API.Settings;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Anichron.API.Tests.Unit.Infrastructure;

/// <summary>
/// Real API endpoints on a test server with the API's own authentication registration, where a
/// request carries a real access token, in the bearer header or in the media cookie.
/// </summary>
internal sealed partial class JwtTestApp : IAsyncDisposable
{
    private const string Secret = "test-secret-that-is-long-enough-for-hmac-sha256-signing";
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";

    private readonly WebApplication app;

    private JwtTestApp(WebApplication app) => this.app = app;

    public HttpClient Client => app.GetTestClient();

    public static Task<JwtTestApp> StartAsync(Action<IEndpointRouteBuilder> mapEndpoints, Action<IServiceCollection> configureServices)
        => StartHostAsync(app => mapEndpoints(app.MapGroup(ApiPaths.Base)), configureServices);

    public static Task<JwtTestApp> StartWholeApiAsync()
        => StartHostAsync(app => app.MapApiEndpoints(), _ => { });

    private static async Task<JwtTestApp> StartHostAsync(Action<WebApplication> mapEndpoints, Action<IServiceCollection> configureServices)
    {
        var app = await ApiTestHost.StartAuthorizedAsync(
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = Secret,
                    ["Jwt:Issuer"] = Issuer,
                    ["Jwt:Audience"] = Audience,
                });
                builder.Services.AddAuthServices(builder.Configuration);
                builder.Services.AddProxyEndpoints();
                configureServices(builder.Services);
            },
            mapEndpoints);
        return new JwtTestApp(app);
    }

    /// <summary>An access token for <paramref name="userId"/>, as the API issues it.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="mustChangePassword">Whether the token says the password must change.</param>
    /// <param name="issuedAt">When it was issued; now by default.</param>
    /// <param name="secret">The signing key; the API's own by default.</param>
    public static string TokenFor(Guid userId, bool mustChangePassword = false, Instant? issuedAt = null, string secret = Secret)
    {
        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(issuedAt ?? SystemClock.Instance.GetCurrentInstant());
        var settings = new JwtSettings { Secret = secret, Issuer = Issuer, Audience = Audience };
        var factory = new JwtFactory(Options.Create(settings), clock, Substitute.For<IGuidFactory>());
        return factory.Create(new User { Id = userId, Username = "mira", MustChangePassword = mustChangePassword });
    }

    public static HttpRequestMessage WithBearer(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public static HttpRequestMessage WithMediaCookie(HttpRequestMessage request, string token)
    {
        request.Headers.Add("Cookie", $"media_token={token}");
        return request;
    }

    public static readonly Guid RouteValue = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    /// <summary>Every mapped route that requires a signed-in user, with each route parameter filled in by <see cref="RouteValue"/>.</summary>
    public IReadOnlyList<(string Method, string Path)> SignedInRoutes()
        => [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null && endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (method, "/" + RouteParameter().Replace(endpoint.RoutePattern.RawText!.Trim('/'), RouteValue.ToString())))
            )];

    public ValueTask DisposeAsync() => app.DisposeAsync();

    [GeneratedRegex("{[^}]+}")]
    private static partial Regex RouteParameter();
}
