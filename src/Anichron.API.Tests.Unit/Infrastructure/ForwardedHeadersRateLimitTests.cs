using Anichron.API.Infrastructure;
using Anichron.API.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class ForwardedHeadersRateLimitTests
{
    private const string PeerHeader = "X-Test-Peer";
    private const string UntrustedPeer = "203.0.113.5";
    private const string TrustedPeer = "10.0.0.2";

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services
            .AddForwardedHeadersSupport(builder.Configuration)
            .AddRateLimiting(builder.Configuration);

        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers[PeerHeader].ToString());
            return next(context);
        });
        app.UseForwardedHeaders();
        app.UseRateLimiter();
        app.MapGet("/probe", (HttpContext context) => context.Connection.RemoteIpAddress!.ToString())
            .RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        app.MapGet("/probe-refresh", () => "ok")
            .RequireRateLimiting(AuthRateLimitPolicies.Refresh);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<List<HttpResponseMessage>> SendWithDistinctForwardedForAsync(
        WebApplication app, string peer, int count)
    {
        using var client = app.GetTestClient();
        var responses = new List<HttpResponseMessage>();
        for (var i = 1; i <= count; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
            request.Headers.Add(PeerHeader, peer);
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{i}");
            responses.Add(await client.SendAsync(request, TestContext.Current.CancellationToken));
        }

        return responses;
    }

    [Fact]
    public async Task UntrustedPeer_SpoofedForwardedFor_SharesOneBucketAsync()
    {
        await using var app = await StartAsync([]);

        var responses = await SendWithDistinctForwardedForAsync(
            app, UntrustedPeer, AppDefaults.RateLimit.Sensitive.PermitLimit + 1);

        responses[^1].StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be(UntrustedPeer);
    }

    [Fact]
    public async Task KnownProxy_ForwardedFor_GetsBucketPerClientAsync()
    {
        await using var app = await StartAsync(new() { ["ForwardedHeaders:KnownProxies"] = TrustedPeer });

        var responses = await SendWithDistinctForwardedForAsync(
            app, TrustedPeer, AppDefaults.RateLimit.Sensitive.PermitLimit + 1);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be("198.51.100.1");
    }

    [Fact]
    public async Task KnownNetwork_Ipv4MappedPeer_IsTrustedAsync()
    {
        await using var app = await StartAsync(new() { ["FORWARDEDHEADERS:KNOWNNETWORKS"] = "10.0.0.0/8, 172.16.0.0/12" });

        var responses = await SendWithDistinctForwardedForAsync(app, $"::ffff:{TrustedPeer}", 1);

        (await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be("198.51.100.1");
    }

    [Fact]
    public async Task RefreshPolicy_AllowsItsPermitLimitThenRejectsAsync()
    {
        await using var app = await StartAsync([]);
        using var client = app.GetTestClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/probe-refresh");
            request.Headers.Add(PeerHeader, UntrustedPeer);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Equal(
            HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task SensitivePolicy_ConfiguredPermitLimit_AllowsThatManyThenRejectsAsync()
    {
        await using var app = await StartAsync(new() { ["RateLimit:SensitivePermitLimit"] = "3" });
        using var client = app.GetTestClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
            request.Headers.Add(PeerHeader, UntrustedPeer);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task SensitivePolicy_PermitLimitBelowOne_FailsAtStartupAsync(string limit)
    {
        var start = () => StartAsync(new() { ["RateLimit:SensitivePermitLimit"] = limit });

        await start.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public void AddForwardedHeadersSupport_UnparseableNetwork_ThrowsAtRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks"] = "not-a-cidr" })
            .Build();

        var act = () => new ServiceCollection().AddForwardedHeadersSupport(configuration);

        act.Should().Throw<FormatException>();
    }
}
