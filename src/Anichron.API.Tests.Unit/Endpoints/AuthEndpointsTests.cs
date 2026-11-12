using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Http;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AuthEndpointsTests
{
    private static readonly RegisterRequest ValidRegisterRequest =
        new("alice", "alice@example.com", "SecurePass1!", "inv-token");

    private static readonly LoginRequest ValidLoginRequest =
        new("alice", "SecurePass1!");

    private static DefaultHttpContext NewHttp() => new();

    private static DefaultHttpContext NewHttpWithCookie(string name, string value)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{name}={value}";
        return httpContext;
    }

    // ==========================================================================
    // RegisterAsync
    // ==========================================================================

    [Fact]
    public async Task RegisterAsync_CallsAuthServiceWithRequestValues()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        auth.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthTokens("access", "refresh", TimeSpan.FromMinutes(15), new User()));

        await AuthEndpoints.RegisterAsync(
            ValidRegisterRequest, auth, mapper, NewHttp(), CancellationToken.None);

        await auth.Received(1).RegisterAsync(
            ValidRegisterRequest.Username, ValidRegisterRequest.Email,
            ValidRegisterRequest.Password, ValidRegisterRequest.InviteToken,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_ReturnsMappedResult()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        ServiceResult<AuthTokens, IRegisterError> serviceResult = new AuthTokens("access", "refresh", TimeSpan.FromMinutes(15), new User());
        var expectedResult = Results.NoContent();
        auth.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(serviceResult);
        mapper.GetRegistrationResult(serviceResult, Arg.Any<HttpContext>())
            .Returns(expectedResult);

        var result = await AuthEndpoints.RegisterAsync(
            ValidRegisterRequest, auth, mapper, NewHttp(), CancellationToken.None);

        result.Should().BeSameAs(expectedResult);
    }

    // ==========================================================================
    // LoginWebAsync
    // ==========================================================================

    [Fact]
    public async Task LoginWebAsync_CallsAuthServiceWithCredentials()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthTokens("access", "refresh", TimeSpan.FromMinutes(15), new User()));

        await AuthEndpoints.LoginWebAsync(ValidLoginRequest, auth, mapper, NewHttp(), CancellationToken.None);

        await auth.Received(1).LoginAsync(
            ValidLoginRequest.UsernameOrEmail, ValidLoginRequest.Password, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginWebAsync_PassesSetCookieTrueToMapper()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthTokens("access", "refresh", TimeSpan.FromMinutes(15), new User()));

        await AuthEndpoints.LoginWebAsync(ValidLoginRequest, auth, mapper, NewHttp(), CancellationToken.None);

        mapper.Received(1).GetLoginResult(Arg.Any<ServiceResult<AuthTokens, ILoginError>>(), Arg.Any<HttpContext>(), setCookie: true);
    }

    // ==========================================================================
    // LoginMobileAsync
    // ==========================================================================

    [Fact]
    public async Task LoginMobileAsync_CallsAuthServiceWithCredentials()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthTokens("access", "refresh", TimeSpan.FromMinutes(15), new User()));

        await AuthEndpoints.LoginMobileAsync(ValidLoginRequest, auth, mapper, NewHttp(), CancellationToken.None);

        await auth.Received(1).LoginAsync(
            ValidLoginRequest.UsernameOrEmail, ValidLoginRequest.Password, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginMobileAsync_PassesSetCookieFalseToMapper()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthTokens("access", "refresh", TimeSpan.FromMinutes(15), new User()));

        await AuthEndpoints.LoginMobileAsync(ValidLoginRequest, auth, mapper, NewHttp(), CancellationToken.None);

        mapper.Received(1).GetLoginResult(Arg.Any<ServiceResult<AuthTokens, ILoginError>>(), Arg.Any<HttpContext>(), setCookie: false);
    }

    // ==========================================================================
    // RefreshAsync
    // ==========================================================================

    [Fact]
    public async Task RefreshAsync_NoCookieAndNoBody_Returns401WithError()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();

        var result = await AuthEndpoints.RefreshAsync(NewHttp(), auth, mapper, request: null, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await auth.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        mapper.DidNotReceive().GetRefreshResult(Arg.Any<ServiceResult<AuthTokens, IRefreshError>>(), Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task RefreshAsync_TokenInCookie_CallsServiceAndDelegatesTheRequest()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        var http = NewHttpWithCookie("refresh_token", "cookie_tok");
        auth.RefreshAsync("cookie_tok", Arg.Any<CancellationToken>())
            .Returns(new AuthTokens("access", "refresh", TimeSpan.FromMinutes(15), new User()));

        await AuthEndpoints.RefreshAsync(http, auth, mapper, request: null, CancellationToken.None);

        await auth.Received(1).RefreshAsync("cookie_tok", Arg.Any<CancellationToken>());
        mapper.Received(1).GetRefreshResult(Arg.Any<ServiceResult<AuthTokens, IRefreshError>>(), http);
    }

    [Fact]
    public async Task RefreshAsync_TokenInBodyOnly_CallsServiceAndDelegatesTheRequest()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        auth.RefreshAsync("body_tok", Arg.Any<CancellationToken>())
            .Returns(new AuthTokens("access", "refresh", TimeSpan.FromMinutes(15), new User()));

        var http = NewHttp();

        await AuthEndpoints.RefreshAsync(http, auth, mapper, new RefreshRequest("body_tok"), CancellationToken.None);

        await auth.Received(1).RefreshAsync("body_tok", Arg.Any<CancellationToken>());
        mapper.Received(1).GetRefreshResult(Arg.Any<ServiceResult<AuthTokens, IRefreshError>>(), http);
    }

    [Fact]
    public async Task RefreshAsync_TokenInCookieAndBody_TheCookieWins()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        var http = NewHttpWithCookie("refresh_token", "cookie_tok");

        await AuthEndpoints.RefreshAsync(http, auth, mapper, new RefreshRequest("body_tok"), CancellationToken.None);

        await auth.Received(1).RefreshAsync("cookie_tok", Arg.Any<CancellationToken>());
        await auth.DidNotReceive().RefreshAsync("body_tok", Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // LogoutAsync
    // ==========================================================================

    [Fact]
    public async Task LogoutAsync_TokenInCookieAndBody_RevokesTheCookieToken()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        var http = NewHttpWithCookie("refresh_token", "cookie_tok");

        await AuthEndpoints.LogoutAsync(http, auth, mapper, new RefreshRequest("body_tok"), CancellationToken.None);

        await auth.Received(1).RevokeAsync("cookie_tok", Arg.Any<CancellationToken>());
        await auth.DidNotReceive().RevokeAsync("body_tok", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogoutAsync_NoCookieAndNoBody_ClearsCookieDoesNotRevokeAndReturns204()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();

        var result = await AuthEndpoints.LogoutAsync(NewHttp(), auth, mapper, request: null, CancellationToken.None);

        mapper.Received(1).ClearSessionCookies(Arg.Any<HttpContext>());
        await auth.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task LogoutAsync_TokenInCookie_RevokesTokenAndReturns204()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();
        var http = NewHttpWithCookie("refresh_token", "cookie_tok");

        var result = await AuthEndpoints.LogoutAsync(http, auth, mapper, request: null, CancellationToken.None);

        mapper.Received(1).ClearSessionCookies(Arg.Any<HttpContext>());
        await auth.Received(1).RevokeAsync("cookie_tok", Arg.Any<CancellationToken>());
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task LogoutAsync_TokenInBodyOnly_RevokesTokenAndReturns204()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<ISessionResponseMapper>();

        var result = await AuthEndpoints.LogoutAsync(
            NewHttp(), auth, mapper, new RefreshRequest("body_tok"), CancellationToken.None);

        mapper.Received(1).ClearSessionCookies(Arg.Any<HttpContext>());
        await auth.Received(1).RevokeAsync("body_tok", Arg.Any<CancellationToken>());
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }
}
