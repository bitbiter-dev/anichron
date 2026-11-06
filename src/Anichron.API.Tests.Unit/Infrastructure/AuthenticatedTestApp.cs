using Anichron.API.Infrastructure;
using Anichron.API.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Anichron.API.Tests.Unit.Infrastructure;

/// <summary>
/// Real API endpoints on a test server, under the API base path, where a request is authenticated
/// as the user named in <see cref="UserHeader"/>, as an admin when it also carries
/// <see cref="AdminHeader"/>. A test maps only the endpoints it exercises, so the wiring of the
/// others is not in its coverage.
/// </summary>
internal sealed class AuthenticatedTestApp : IAsyncDisposable
{
    public const string UserHeader = "X-Test-User";
    public const string AdminHeader = "X-Test-Admin";
    public const string MustChangeHeader = "X-Test-Must-Change";

    private readonly WebApplication app;

    private AuthenticatedTestApp(WebApplication app) => this.app = app;

    public HttpClient Client => app.GetTestClient();

    public static async Task<AuthenticatedTestApp> StartAsync(
        Action<IEndpointRouteBuilder> mapEndpoints, Action<IServiceCollection> configureServices, bool rateLimited = false)
    {
        var app = await ApiTestHost.StartAuthorizedAsync(
            builder =>
            {
                builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", null);
                configureServices(builder.Services);
            },
            app => mapEndpoints(app.MapGroup(ApiPaths.Base)),
            rateLimited);
        return new AuthenticatedTestApp(app);
    }

    public static HttpRequestMessage Get(string path, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(UserHeader, userId.ToString());
        return request;
    }

    public static HttpRequestMessage GetAsAdmin(string path, Guid userId)
    {
        var request = Get(path, userId);
        request.Headers.Add(AdminHeader, "true");
        return request;
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());

            List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId.ToString())];
            if (Request.Headers.ContainsKey(AdminHeader))
                claims.Add(new Claim(AppClaimTypes.IsAdmin, "true"));
            if (Request.Headers.TryGetValue(MustChangeHeader, out var mustChange))
                claims.Add(new Claim(AppClaimTypes.MustChangePassword, mustChange.ToString()));
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
