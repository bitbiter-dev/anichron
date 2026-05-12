using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class MustChangePasswordMiddlewareTests
{
    private const string ChangePasswordPath = "/api/v1/users/me/password";
    private const string LogoutPath = "/api/v1/auth/logout";

    private static DefaultHttpContext BuildContext(ClaimsPrincipal user, string method, string path)
    {
        var context = new DefaultHttpContext { User = user };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static ClaimsPrincipal Unauthenticated()
        => new(new ClaimsIdentity());

    private static Claim MustChangeClaim() => new("must_change_password", "true");

    private static async Task<string> ReadBodyAsync(HttpResponse response)
    {
        response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(response.Body).ReadToEndAsync();
    }

    // ==========================================================================
    // Calls next
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_Unauthenticated_CallsNext()
    {
        var context = BuildContext(Unauthenticated(), HttpMethods.Get, "/api/v1/some/endpoint");
        var nextCalled = false;
        var middleware = new MustChangePasswordMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithoutClaim_CallsNext()
    {
        var context = BuildContext(Authenticated(), HttpMethods.Get, "/api/v1/some/endpoint");
        var nextCalled = false;
        var middleware = new MustChangePasswordMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithClaimValueFalse_CallsNext()
    {
        var context = BuildContext(
            Authenticated(new Claim("must_change_password", "false")),
            HttpMethods.Get, "/api/v1/some/endpoint");
        var nextCalled = false;
        var middleware = new MustChangePasswordMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithClaim_PostChangePasswordPath_CallsNext()
    {
        var context = BuildContext(Authenticated(MustChangeClaim()), HttpMethods.Post, ChangePasswordPath);
        var nextCalled = false;
        var middleware = new MustChangePasswordMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithClaim_PostLogoutPath_CallsNext()
    {
        var context = BuildContext(Authenticated(MustChangeClaim()), HttpMethods.Post, LogoutPath);
        var nextCalled = false;
        var middleware = new MustChangePasswordMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithClaim_GetMePath_CallsNext()
    {
        var context = BuildContext(Authenticated(MustChangeClaim()), HttpMethods.Get, "/api/v1/users/me");
        var nextCalled = false;
        var middleware = new MustChangePasswordMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    // ==========================================================================
    // Returns 403
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithClaim_NonExemptGetPath_Returns403WithMessage()
    {
        var context = BuildContext(Authenticated(MustChangeClaim()), HttpMethods.Get, "/api/v1/some/endpoint");
        var nextCalled = false;
        var middleware = new MustChangePasswordMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);
        var body = await ReadBodyAsync(context.Response);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
        body.Should().Contain("Password change required before continuing.");
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithClaim_NonExemptPostPath_Returns403()
    {
        var context = BuildContext(Authenticated(MustChangeClaim()), HttpMethods.Post, "/api/v1/some/endpoint");
        var nextCalled = false;
        var middleware = new MustChangePasswordMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithClaim_GetChangePasswordPath_Returns403()
    {
        var context = BuildContext(Authenticated(MustChangeClaim()), HttpMethods.Get, ChangePasswordPath);
        var nextCalled = false;
        var middleware = new MustChangePasswordMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }
}
