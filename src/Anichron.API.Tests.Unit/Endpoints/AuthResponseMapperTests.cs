using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AuthResponseMapperTests
{
    private static readonly Instant FixedNow = Instant.FromUtc(2026, 1, 1, 12, 0, 0);

    private sealed class TestFixture
    {
        private readonly IClock _clock = Substitute.For<IClock>();
        private readonly AuthCookieSettings _cookieSettings = new()
        {
            SameSite = SameSiteMode.Strict,
            RefreshTokenDays = 7,
        };

        public TestFixture() => _clock.GetCurrentInstant().Returns(FixedNow);

        public AuthResponseMapper CreateTestee() => new(_cookieSettings, _clock);
    }

    private static DefaultHttpContext NewHttp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<JsonOptions>(_ => { });
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static async Task<string> ReadBodyAsync(HttpResponse response)
    {
        response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(response.Body).ReadToEndAsync();
    }

    // ==========================================================================
    // GetRegistrationResult
    // ==========================================================================

    [Fact]
    public async Task GetRegistrationResult_Success_Returns200SetsRefreshCookieAndOmitsTokenFromBody()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetRegistrationResult(
            AuthResult.Ok(new AuthTokens("acc_token", "ref_token")),
            http, new PasswordPolicy(), new UsernamePolicy());
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);
        var setCookie = http.Response.Headers.SetCookie.ToString();

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(200);
            setCookie.Should().Contain("refresh_token=ref_token");
            setCookie.Should().ContainAll("httponly", "secure", "samesite=strict");
            body.Should().Contain("acc_token");
            body.Should().NotContain("ref_token");
        });
    }

    [Theory]
    [InlineData(AuthError.UsernameTaken, 409)]
    [InlineData(AuthError.EmailTaken, 409)]
    [InlineData(AuthError.InvalidUsername, 422)]
    [InlineData(AuthError.InvalidEmail, 422)]
    [InlineData(AuthError.PasswordTooShort, 422)]
    [InlineData(AuthError.PasswordTooLong, 422)]
    [InlineData(AuthError.PasswordPwned, 422)]
    public async Task GetRegistrationResult_Failure_ReturnsExpectedStatusCode(AuthError error, int expectedStatus)
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetRegistrationResult(
            AuthResult.Fail<AuthTokens>(error),
            http, new PasswordPolicy(), new UsernamePolicy());
        await result.ExecuteAsync(http);

        http.Response.StatusCode.Should().Be(expectedStatus);
    }

    // ==========================================================================
    // GetLoginResult
    // ==========================================================================

    [Fact]
    public async Task GetLoginResult_SuccessWithCookie_Returns200SetsRefreshCookieAndOmitsTokenFromBody()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetLoginResult(
            AuthResult.Ok(new AuthTokens("acc_token", "ref_token")), http, setCookie: true);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);
        var setCookie = http.Response.Headers.SetCookie.ToString();

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(200);
            setCookie.Should().Contain("refresh_token=ref_token");
            body.Should().Contain("acc_token");
            body.Should().NotContain("ref_token");
        });
    }

    [Fact]
    public async Task GetLoginResult_SuccessWithoutCookie_Returns200AndIncludesTokenInBody()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetLoginResult(
            AuthResult.Ok(new AuthTokens("acc_token", "ref_token")), http, setCookie: false);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(200);
            http.Response.Headers.SetCookie.ToString().Should().NotContain("refresh_token");
            body.Should().Contain("acc_token");
            body.Should().Contain("ref_token");
        });
    }

    [Theory]
    [InlineData(AuthError.InvalidCredentials)]
    [InlineData(AuthError.AccountDisabled)]
    public async Task GetLoginResult_AuthFailure_Returns401(AuthError error)
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetLoginResult(AuthResult.Fail<AuthTokens>(error), http, setCookie: false);
        await result.ExecuteAsync(http);

        http.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetLoginResult_AccountTemporarilyLocked_Returns429WithRetryAfterHeader()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetLoginResult(AuthResult.Locked<AuthTokens>(60), http, setCookie: false);
        await result.ExecuteAsync(http);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(429);
            http.Response.Headers.RetryAfter.ToString().Should().Be("60");
        });
    }

    // ==========================================================================
    // GetRefreshResult
    // ==========================================================================

    [Fact]
    public async Task GetRefreshResult_SuccessWithCookie_Returns200AndSetsRefreshCookie()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetRefreshResult(
            AuthResult.Ok(new AuthTokens("acc_token", "ref_token")), http, setCookie: true);
        await result.ExecuteAsync(http);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(200);
            http.Response.Headers.SetCookie.ToString().Should().Contain("refresh_token=ref_token");
        });
    }

    [Fact]
    public async Task GetRefreshResult_SuccessWithoutCookie_Returns200AndIncludesTokenInBody()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetRefreshResult(
            AuthResult.Ok(new AuthTokens("acc_token", "ref_token")), http, setCookie: false);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(200);
            body.Should().Contain("ref_token");
        });
    }

    [Theory]
    [InlineData(AuthError.TokenInvalid)]
    [InlineData(AuthError.AccountDisabled)]
    public async Task GetRefreshResult_AuthFailure_Returns401(AuthError error)
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetRefreshResult(AuthResult.Fail<AuthTokens>(error), http, setCookie: false);
        await result.ExecuteAsync(http);

        http.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetRefreshResult_AccountTemporarilyLocked_Returns429WithRetryAfterHeader()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetRefreshResult(AuthResult.Locked<AuthTokens>(30), http, setCookie: false);
        await result.ExecuteAsync(http);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(429);
            http.Response.Headers.RetryAfter.ToString().Should().Be("30");
        });
    }

    [Fact]
    public async Task GetLoginResult_AccountTemporarilyLockedForOneSecond_ReturnsSingularSecondMessage()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetLoginResult(AuthResult.Locked<AuthTokens>(1), http, setCookie: false);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(429);
            body.Should().Contain("1 second");
            body.Should().NotContain("seconds");
        });
    }

    [Fact]
    public void GetRegistrationResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetRegistrationResult(
            AuthResult.Fail<AuthTokens>(AuthError.InvalidCredentials),
            NewHttp(), new PasswordPolicy(), new UsernamePolicy());

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetRegistrationResult_UndefinedAuthError_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetRegistrationResult(
            AuthResult.Fail<AuthTokens>((AuthError)999),
            NewHttp(), new PasswordPolicy(), new UsernamePolicy());

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetLoginResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetLoginResult(
            AuthResult.Fail<AuthTokens>(AuthError.UsernameTaken),
            NewHttp(), setCookie: false);

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetLoginResult_UndefinedAuthError_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetLoginResult(
            AuthResult.Fail<AuthTokens>((AuthError)999),
            NewHttp(), setCookie: false);

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetRefreshResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetRefreshResult(
            AuthResult.Fail<AuthTokens>(AuthError.UsernameTaken),
            NewHttp(), setCookie: false);

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetRefreshResult_UndefinedAuthError_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetRefreshResult(
            AuthResult.Fail<AuthTokens>((AuthError)999),
            NewHttp(), setCookie: false);

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    // ==========================================================================
    // ClearRefreshCookie
    // ==========================================================================

    [Fact]
    public void ClearRefreshCookie_DeletesRefreshTokenCookieFromResponse()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        testee.ClearRefreshCookie(http);

        http.Response.Headers.SetCookie.ToString().Should().Contain(AuthMessages.RefreshTokenCookieName);
    }

    // ==========================================================================
    // GetChangePasswordResult
    // ==========================================================================

    [Fact]
    public async Task GetChangePasswordResult_Success_Returns204NoContent()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetChangePasswordResult(AuthResult.Ok(), new PasswordPolicy());
        await result.ExecuteAsync(http);

        http.Response.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task GetChangePasswordResult_InvalidCredentials_Returns400WithMessage()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetChangePasswordResult(AuthResult.Fail(AuthError.InvalidCredentials), new PasswordPolicy());
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(400);
            body.Should().Contain(AuthMessages.InvalidCredentials);
        });
    }

    [Fact]
    public async Task GetChangePasswordResult_PasswordTooShort_Returns422WithPolicyTooShortMessage()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();
        var policy = new PasswordPolicy { MinLength = 16 };

        var result = testee.GetChangePasswordResult(AuthResult.Fail(AuthError.PasswordTooShort), policy);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(422);
            body.Should().Contain(policy.TooShortMessage);
        });
    }

    [Fact]
    public async Task GetChangePasswordResult_PasswordTooLong_Returns422WithPolicyTooLongMessage()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();
        var policy = new PasswordPolicy { MaxLength = 64 };

        var result = testee.GetChangePasswordResult(AuthResult.Fail(AuthError.PasswordTooLong), policy);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(422);
            body.Should().Contain(policy.TooLongMessage);
        });
    }

    [Fact]
    public async Task GetChangePasswordResult_PasswordPwned_Returns422WithPwnedMessage()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetChangePasswordResult(AuthResult.Fail(AuthError.PasswordPwned), new PasswordPolicy());
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(422);
            body.Should().Contain(AuthMessages.PasswordPwned);
        });
    }

    [Fact]
    public void GetChangePasswordResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetChangePasswordResult(AuthResult.Fail(AuthError.UsernameTaken), new PasswordPolicy());

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetChangePasswordResult_UndefinedAuthError_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetChangePasswordResult(AuthResult.Fail((AuthError)999), new PasswordPolicy());

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }
}
