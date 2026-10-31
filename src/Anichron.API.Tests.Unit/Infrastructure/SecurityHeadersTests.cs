using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.TestHost;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class SecurityHeadersTests
{
    private const string AppPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; media-src 'self' blob:; "
        + "connect-src 'self'; worker-src 'self'; manifest-src 'self'; font-src 'self'; object-src 'none'; base-uri 'none'; "
        + "form-action 'self'; frame-ancestors 'none'; require-trusted-types-for 'script'";

    private const string AppPermissions =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        var builder = ApiTestHost.CreateBuilder(Path.Combine(AppContext.BaseDirectory, "Infrastructure", "TestWebRoot"));
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseSecurityHeaders();
        app.UseUi();
        app.MapApiEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(method, path);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        await response.Content.LoadIntoBufferAsync(TestContext.Current.CancellationToken);
        return response;
    }

    private static string Header(HttpResponseMessage response, string name)
        => response.Headers.Should().ContainKey(name).WhoseValue.Should().ContainSingle().Which;

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("GET", "/login")]
    [InlineData("GET", "/index.html")]
    [InlineData("GET", "/assets/index-Bx7f3k9q.js")]
    [InlineData("GET", "/missing.js")]
    [InlineData("POST", "/login")]
    [InlineData("GET", "/apiary")]
    public async Task AppResponse_CarriesTheFullPolicyAsync(string method, string path)
    {
        using var response = await SendAsync(new HttpMethod(method), path);

        Header(response, "Content-Security-Policy").Should().Be(AppPolicy);
        Header(response, "Referrer-Policy").Should().Be("same-origin");
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "Permissions-Policy").Should().Be(AppPermissions);
    }

    [Theory]
    [InlineData("GET", "/api/v1/healthz")]
    [InlineData("GET", "/api/v1/nothing-here")]
    [InlineData("GET", "/api/v1/auth/login")]
    [InlineData("GET", "/api")]
    public async Task ApiResponse_CarriesNoSniffAndFrameAncestorsNoneAsync(string method, string path)
    {
        using var response = await SendAsync(new HttpMethod(method), path);

        Header(response, "Content-Security-Policy").Should().Be("frame-ancestors 'none'");
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        response.Headers.Contains("Permissions-Policy").Should().BeFalse();
        response.Headers.Contains("Referrer-Policy").Should().BeFalse();
    }
}
