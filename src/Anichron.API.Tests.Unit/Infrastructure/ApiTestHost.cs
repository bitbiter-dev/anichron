using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.Core.Data.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Anichron.API.Tests.Unit.Infrastructure;

internal static class ApiTestHost
{
    public static WebApplicationBuilder CreateBuilder(string? webRootPath = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { WebRootPath = webRootPath });
        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton(Substitute.For<IAuthService>());
        builder.Services.AddSingleton(Substitute.For<IAuthResponseMapper>());
        builder.Services.AddSingleton(Substitute.For<IAdminResetService>());
        builder.Services.AddSingleton(Substitute.For<IAdminUserService>());
        builder.Services.AddSingleton(Substitute.For<IAdminStorageConfigService>());
        builder.Services.AddSingleton(Substitute.For<IUserRepository>());
        builder.Services.AddSingleton(Substitute.For<IMediaAssetReadRepository>());
        return builder;
    }

    /// <summary>A started test server with the API's authorization and problem pipeline around <paramref name="mapEndpoints"/>.</summary>
    /// <param name="configure">Registers the authentication and the services the endpoints use.</param>
    /// <param name="mapEndpoints">Maps the endpoints under test.</param>
    /// <param name="rateLimited">Whether the API's rate limiting runs too.</param>
    public static async Task<WebApplication> StartAuthorizedAsync(
        Action<WebApplicationBuilder> configure, Action<WebApplication> mapEndpoints, bool rateLimited = false)
    {
        var builder = CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationPolicies();
        builder.Services.AddApiProblems();
        if (rateLimited)
            builder.Services.AddRateLimiting(builder.Configuration);
        configure(builder);
        var app = builder.Build();
        app.UseApiProblems();
        if (rateLimited)
            app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        mapEndpoints(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
