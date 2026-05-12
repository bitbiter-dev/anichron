using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

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
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = $"{name}={value}";
        return ctx;
    }

    // ==========================================================================
    // RegisterAsync
    // ==========================================================================

    [Fact]
    public async Task RegisterAsync_CallsAuthServiceWithRequestValues()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        auth.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(new AuthTokens("access", "refresh")));

        await AuthEndpoints.RegisterAsync(
            ValidRegisterRequest, auth, mapper, NewHttp(),
            Options.Create(new PasswordPolicy()), Options.Create(new UsernamePolicy()),
            CancellationToken.None);

        await auth.Received(1).RegisterAsync(
            ValidRegisterRequest.Username, ValidRegisterRequest.Email,
            ValidRegisterRequest.Password, ValidRegisterRequest.InviteToken,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_ReturnsMappedResult()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var serviceResult = AuthResult.Ok(new AuthTokens("access", "refresh"));
        var expectedResult = Results.NoContent();
        auth.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(serviceResult);
        mapper.GetRegistrationResult(serviceResult, Arg.Any<HttpContext>(), Arg.Any<PasswordPolicy>(), Arg.Any<UsernamePolicy>())
            .Returns(expectedResult);

        var result = await AuthEndpoints.RegisterAsync(
            ValidRegisterRequest, auth, mapper, NewHttp(),
            Options.Create(new PasswordPolicy()), Options.Create(new UsernamePolicy()),
            CancellationToken.None);

        result.Should().BeSameAs(expectedResult);
    }

    // ==========================================================================
    // LoginWebAsync
    // ==========================================================================

    [Fact]
    public async Task LoginWebAsync_CallsAuthServiceWithCredentials()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(new AuthTokens("access", "refresh")));

        await AuthEndpoints.LoginWebAsync(ValidLoginRequest, auth, mapper, NewHttp(), CancellationToken.None);

        await auth.Received(1).LoginAsync(
            ValidLoginRequest.UsernameOrEmail, ValidLoginRequest.Password, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginWebAsync_PassesSetCookieTrueToMapper()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(new AuthTokens("access", "refresh")));

        await AuthEndpoints.LoginWebAsync(ValidLoginRequest, auth, mapper, NewHttp(), CancellationToken.None);

        mapper.Received(1).GetLoginResult(Arg.Any<AuthResult<AuthTokens>>(), Arg.Any<HttpContext>(), setCookie: true);
    }

    // ==========================================================================
    // LoginMobileAsync
    // ==========================================================================

    [Fact]
    public async Task LoginMobileAsync_CallsAuthServiceWithCredentials()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(new AuthTokens("access", "refresh")));

        await AuthEndpoints.LoginMobileAsync(ValidLoginRequest, auth, mapper, NewHttp(), CancellationToken.None);

        await auth.Received(1).LoginAsync(
            ValidLoginRequest.UsernameOrEmail, ValidLoginRequest.Password, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginMobileAsync_PassesSetCookieFalseToMapper()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(new AuthTokens("access", "refresh")));

        await AuthEndpoints.LoginMobileAsync(ValidLoginRequest, auth, mapper, NewHttp(), CancellationToken.None);

        mapper.Received(1).GetLoginResult(Arg.Any<AuthResult<AuthTokens>>(), Arg.Any<HttpContext>(), setCookie: false);
    }

    // ==========================================================================
    // RefreshAsync
    // ==========================================================================

    [Fact]
    public async Task RefreshAsync_NoCookieAndNoBody_Returns401WithError()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();

        var result = await AuthEndpoints.RefreshAsync(NewHttp(), auth, mapper, req: null, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await auth.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        mapper.DidNotReceive().GetRefreshResult(Arg.Any<AuthResult<AuthTokens>>(), Arg.Any<HttpContext>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task RefreshAsync_TokenInCookie_CallsServiceAndDelegatesWithSetCookieTrue()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var http = NewHttpWithCookie(AuthMessages.RefreshTokenCookieName, "cookie_tok");
        auth.RefreshAsync("cookie_tok", Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(new AuthTokens("access", "refresh")));

        await AuthEndpoints.RefreshAsync(http, auth, mapper, req: null, CancellationToken.None);

        await auth.Received(1).RefreshAsync("cookie_tok", Arg.Any<CancellationToken>());
        mapper.Received(1).GetRefreshResult(Arg.Any<AuthResult<AuthTokens>>(), Arg.Any<HttpContext>(), setCookie: true);
    }

    [Fact]
    public async Task RefreshAsync_TokenInBodyOnly_CallsServiceAndDelegatesWithSetCookieFalse()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        auth.RefreshAsync("body_tok", Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(new AuthTokens("access", "refresh")));

        await AuthEndpoints.RefreshAsync(NewHttp(), auth, mapper, new RefreshRequest("body_tok"), CancellationToken.None);

        await auth.Received(1).RefreshAsync("body_tok", Arg.Any<CancellationToken>());
        mapper.Received(1).GetRefreshResult(Arg.Any<AuthResult<AuthTokens>>(), Arg.Any<HttpContext>(), setCookie: false);
    }

    // ==========================================================================
    // LogoutAsync
    // ==========================================================================

    [Fact]
    public async Task LogoutAsync_NoCookieAndNoBody_ClearsCookieDoesNotRevokeAndReturns204()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();

        var result = await AuthEndpoints.LogoutAsync(NewHttp(), auth, mapper, req: null, CancellationToken.None);

        mapper.Received(1).ClearRefreshCookie(Arg.Any<HttpContext>());
        await auth.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task LogoutAsync_TokenInCookie_RevokesTokenAndReturns204()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();
        var http = NewHttpWithCookie(AuthMessages.RefreshTokenCookieName, "cookie_tok");

        var result = await AuthEndpoints.LogoutAsync(http, auth, mapper, req: null, CancellationToken.None);

        mapper.Received(1).ClearRefreshCookie(Arg.Any<HttpContext>());
        await auth.Received(1).RevokeAsync("cookie_tok", Arg.Any<CancellationToken>());
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task LogoutAsync_TokenInBodyOnly_RevokesTokenAndReturns204()
    {
        var auth = Substitute.For<IAuthService>();
        var mapper = Substitute.For<IAuthResponseMapper>();

        var result = await AuthEndpoints.LogoutAsync(
            NewHttp(), auth, mapper, new RefreshRequest("body_tok"), CancellationToken.None);

        mapper.Received(1).ClearRefreshCookie(Arg.Any<HttpContext>());
        await auth.Received(1).RevokeAsync("body_tok", Arg.Any<CancellationToken>());
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }
}
