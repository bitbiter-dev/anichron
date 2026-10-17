using Anichron.API.Infrastructure;
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
/// as the user named in <see cref="UserHeader"/>. A test maps only the endpoints it exercises, so
/// the wiring of the others is not in its coverage.
/// </summary>
internal sealed class AuthenticatedTestApp : IAsyncDisposable
{
    public const string UserHeader = "X-Test-User";

    private readonly WebApplication app;

    private AuthenticatedTestApp(WebApplication app) => this.app = app;

    public HttpClient Client => app.GetTestClient();

    public static async Task<AuthenticatedTestApp> StartAsync(
        Action<IEndpointRouteBuilder> mapEndpoints, Action<IServiceCollection> configureServices)
    {
        var builder = ApiTestHost.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", null);
        builder.Services.AddAuthorization();
        configureServices(builder.Services);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        mapEndpoints(app.MapGroup(ApiPaths.Base));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new AuthenticatedTestApp(app);
    }

    public static HttpRequestMessage Get(string path, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(UserHeader, userId.ToString());
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

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
