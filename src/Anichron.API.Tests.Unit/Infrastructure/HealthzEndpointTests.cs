using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using System.Text.Json;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class HealthzEndpointTests
{
    [Fact]
    public async Task Healthz_RespondsWithTheJsonReportAndNoCachingAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = ApiTestHost.CreateBuilder();
        builder.WebHost.UseTestServer();
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
