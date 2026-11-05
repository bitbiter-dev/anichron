using Anichron.API.Infrastructure;
using Anichron.API.Tests.Unit.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Net;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class MustChangePasswordTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static Task<AuthenticatedTestApp> StartAsync()
        => AuthenticatedTestApp.StartAsync(api =>
        {
            api.MapGet("/user", () => Results.Ok()).RequireUser();
            api.MapGet("/allowed", () => Results.Ok()).RequireUserAllowedWhileMustChangePassword();
            api.MapGet("/admin", () => Results.Ok()).RequireAdmin();
            api.MapGet("/anonymous", () => Results.Ok()).AllowAnonymous();
        }, _ => { });

    private static HttpRequestMessage Get(string path, string? mustChange = "true", bool admin = false)
    {
        var request = AuthenticatedTestApp.Get($"/api/v1{path}", UserId);
        if (mustChange is not null)
            request.Headers.Add(AuthenticatedTestApp.MustChangeHeader, mustChange);
        if (admin)
            request.Headers.Add(AuthenticatedTestApp.AdminHeader, "true");
        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        await using var app = await StartAsync();
        using var client = app.Client;
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("/user", false)]
    [InlineData("/admin", true)]
    public async Task MustChange_ProtectedRoute_IsAPasswordChangeRequiredProblemAsync(string path, bool admin)
    {
        using var response = await SendAsync(Get(path, admin: admin));

        await ProblemAssertions.AssertProblemAsync(
            response, 403, "password_change_required", "Password change required before continuing.");
    }

    [Fact]
    public async Task MustChange_RouteAllowedWhileMustChangePassword_IsServedAsync()
    {
        using var response = await SendAsync(Get("/allowed"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MustChange_AnonymousRoute_IsServedAsync()
    {
        using var response = await SendAsync(Get("/anonymous"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task WithoutTheFlag_ProtectedRoute_IsServedAsync(string? mustChange)
    {
        using var response = await SendAsync(Get("/user", mustChange));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithoutTheFlag_AdminRouteAsANonAdmin_IsAPlainForbiddenProblemAsync()
    {
        using var response = await SendAsync(Get("/admin", mustChange: null));

        await ProblemAssertions.AssertStatusProblemAsync(response, 403);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().NotContain("password_change_required");
    }

    [Fact]
    public async Task Anonymous_ProtectedRoute_IsUnauthorizedAsync()
    {
        await using var app = await StartAsync();
        using var client = app.Client;

        using var response = await client.GetAsync("/api/v1/user", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
