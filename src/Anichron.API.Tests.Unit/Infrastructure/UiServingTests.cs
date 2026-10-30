using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Text.Json;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class UiServingTests
{
    private static readonly string TestWebRoot = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "TestWebRoot");

    private static async Task<WebApplication> StartAsync(string webRoot)
    {
        var builder = ApiTestHost.CreateBuilder(webRoot);
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseUi();
        app.MapApiEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(string path, string? webRoot = null)
    {
        await using var app = await StartAsync(webRoot ?? TestWebRoot);
        using var client = app.GetTestClient();
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        await response.Content.LoadIntoBufferAsync(TestContext.Current.CancellationToken);
        return response;
    }

    [Fact]
    public async Task HashedAsset_IsServedImmutableForAYearAsync()
    {
        using var response = await GetAsync("/assets/index-Bx7f3k9q.js");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("fixture asset");
        response.Headers.GetValues("Cache-Control").Should().Equal("public, max-age=31536000, immutable");
    }

    [Theory]
    [InlineData("/index.html", "fixture index")]
    [InlineData("/sw.js", "addEventListener")]
    [InlineData("/manifest.webmanifest", "\"name\":\"fixture\"")]
    public async Task UnhashedFile_IsServedNoCacheAsync(string path, string content)
    {
        using var response = await GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(content);
        response.Headers.GetValues("Cache-Control").Should().Equal("no-cache");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/flashbacks/2024/06")]
    public async Task PathWithoutExtension_ServesIndexNoCacheAsync(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("fixture index");
        response.Headers.GetValues("Cache-Control").Should().Equal("no-cache");
    }

    [Fact]
    public async Task PathOutsideApiThatOnlyStartsWithApi_ServesIndexAsync()
    {
        using var response = await GetAsync("/apiary");

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("fixture index");
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/v1/nothing-here")]
    [InlineData("/api/v2/users")]
    [InlineData("/api/v1/nothing-here.json")]
    public async Task UnknownApiPath_IsAJsonNotFoundAsync(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("status").GetInt32().Should().Be(404);
    }

    [Fact]
    public async Task UnknownApiPath_IsAJsonNotFoundForEveryMethodAsync()
    {
        await using var app = await StartAsync(TestWebRoot);
        using var client = app.GetTestClient();

        using var response = await client.DeleteAsync("/api/v1/nothing-here", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task KnownApiRouteWithTheWrongMethod_StaysMethodNotAllowedAsync()
    {
        using var response = await GetAsync("/api/v1/auth/login");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("/missing.js")]
    [InlineData("/assets/index-Missing0.js")]
    [InlineData("/favicon.ico")]
    public async Task UnknownPathWithAnExtension_IsAPlainNotFoundAsync(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentLength.Should().Be(0);
    }

    [Fact]
    public async Task PathWithoutExtension_IsNotFoundForAMethodOtherThanGetOrHeadAsync()
    {
        await using var app = await StartAsync(TestWebRoot);
        using var client = app.GetTestClient();

        using var response = await client.PostAsync("/login", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentLength.Should().Be(0);
    }

    [Fact]
    public async Task PathWithoutExtension_ServesIndexToHeadAsync()
    {
        await using var app = await StartAsync(TestWebRoot);
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Head, "/login");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task WithoutABuiltUi_TheApiStillServesAndTheAppIsNotFoundAsync()
    {
        var missing = Path.Combine(AppContext.BaseDirectory, "no-ui-built");

        using var health = await GetAsync("/api/v1/healthz", missing);
        using var app = await GetAsync("/", missing);

        health.StatusCode.Should().Be(HttpStatusCode.OK);
        app.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
