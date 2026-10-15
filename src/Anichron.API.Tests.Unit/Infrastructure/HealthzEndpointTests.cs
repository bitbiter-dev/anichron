using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.Core.Data.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class HealthzEndpointTests
{
    [Fact]
    public async Task Healthz_RespondsWithTheJsonReportAndNoCachingAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton(Substitute.For<IAuthService>());
        builder.Services.AddSingleton(Substitute.For<IAuthResponseMapper>());
        builder.Services.AddSingleton(Substitute.For<IAdminResetService>());
        builder.Services.AddSingleton(Substitute.For<IAdminUserService>());
        builder.Services.AddSingleton(Substitute.For<IAdminStorageConfigService>());
        builder.Services.AddSingleton(Substitute.For<IUserRepository>());
        await using var app = builder.Build();
        app.MapApiEndpoints();
        await app.StartAsync(ct);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/v1/healthz", ct);

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        body.RootElement.GetProperty("status").GetString().Should().Be("healthy");
        body.RootElement.GetProperty("components").EnumerateObject().Should().BeEmpty();
    }
}
