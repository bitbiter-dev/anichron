using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class SessionResponseMapperTests
{
    private static readonly Instant FixedNow = Instant.FromUtc(2026, 1, 1, 12, 0, 0);

    private sealed class TestFixture
    {
        private readonly IClock _clock = Substitute.For<IClock>();
        private readonly AuthCookieSettings _cookieSettings;
        private readonly PasswordPolicy _passwordPolicy;

        public TestFixture(SameSiteMode sameSite = SameSiteMode.Strict, PasswordPolicy? passwordPolicy = null)
        {
            _passwordPolicy = passwordPolicy ?? new PasswordPolicy();
            _clock.GetCurrentInstant().Returns(FixedNow);
            _cookieSettings = new() { SameSite = sameSite, AccessTokenMinutes = 15, RefreshTokenDays = 7 };
        }

        public IUserStorageConfigRepository StorageConfigs { get; } = Substitute.For<IUserStorageConfigRepository>();

        public SessionResponseMapper CreateTestee()
            => new(_cookieSettings, _clock, new UserProfiles(StorageConfigs), Options.Create(new UsernamePolicy()), Options.Create(_passwordPolicy));
    }

    private static DefaultHttpContext NewHttp(bool withRefreshCookie = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<JsonOptions>(_ => { });
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        if (withRefreshCookie)
            http.Request.Headers.Cookie = "refresh_token=old_token";
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static string[] SetCookiesNamed(HttpContext http, string name)
        => [.. http.Response.Headers.SetCookie.Select(header => header!).Where(header => header.StartsWith($"{name}=", StringComparison.Ordinal))];

    private static Task<string> ReadBodyAsync(HttpResponse response)
    {
        response.Body.Seek(0, SeekOrigin.Begin);
        return new StreamReader(response.Body).ReadToEndAsync();
    }

    // ==========================================================================
    // GetRegistrationResult
    // ==========================================================================

    [Fact]
    public async Task GetRegistrationResult_Success_Returns200SetsRefreshCookieAndOmitsTokenFromBody()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = await testee.GetRegistrationResultAsync(
            new AuthTokens("acc_token", "ref_token", TimeSpan.FromMinutes(15), new User()),
            http);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(200);
            SetCookiesNamed(http, "refresh_token").Should().Equal(
                "refresh_token=ref_token; expires=Thu, 08 Jan 2026 12:00:00 GMT; path=/; secure; samesite=strict; httponly");
            body.Should().Contain("acc_token");
            body.Should().NotContain("ref_token");
        });
    }

    // ==========================================================================
    // GetLoginResult
    // ==========================================================================

    [Fact]
    public async Task GetLoginResult_SuccessWithCookie_Returns200SetsRefreshCookieAndOmitsTokenFromBody()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        var result = await testee.GetLoginResultAsync(
            new AuthTokens("acc_token", "ref_token", TimeSpan.FromMinutes(15), new User()), http, setCookie: true);
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

        var result = await testee.GetLoginResultAsync(
            new AuthTokens("acc_token", "ref_token", TimeSpan.FromMinutes(15), new User()), http, setCookie: false);
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

    // ==========================================================================
    // GetRefreshResult
    // ==========================================================================

    [Fact]
    public async Task GetRefreshResult_SuccessWithCookie_Returns200AndSetsRefreshCookie()
    {
        var http = NewHttp(withRefreshCookie: true);
        var testee = new TestFixture().CreateTestee();

        var result = await testee.GetRefreshResultAsync(
            new AuthTokens("acc_token", "ref_token", TimeSpan.FromMinutes(15), new User()), http);
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

        var result = await testee.GetRefreshResultAsync(
            new AuthTokens("acc_token", "ref_token", TimeSpan.FromMinutes(15), new User()), http);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(200);
            body.Should().Contain("ref_token");
        });
    }

    // ==========================================================================
    // The media cookie (ADR 0013)
    // ==========================================================================

    public static TheoryData<string, SameSiteMode, string> SignInsThatSetTheMediaCookie => new()
    {
        { "register", SameSiteMode.Strict, "strict" },
        { "login", SameSiteMode.Strict, "strict" },
        { "refresh", SameSiteMode.Strict, "strict" },
        { "login", SameSiteMode.None, "none" },
        { "refresh", SameSiteMode.None, "none" },
        { "change-password", SameSiteMode.Strict, "strict" },
    };

    private static async Task<HttpContext> SignInAsync(SessionResponseMapper testee, string kind, bool setCookie, User? user = null)
    {
        var tokens = new AuthTokens("acc_token", "ref_token", TimeSpan.FromMinutes(15), user ?? new User());
        var http = NewHttp(withRefreshCookie: setCookie);
        var result = kind switch
        {
            "register" => await testee.GetRegistrationResultAsync(tokens, http),
            "login" => await testee.GetLoginResultAsync(tokens, http, setCookie),
            "change-password" => await testee.GetChangePasswordResultAsync(tokens, http),
            _ => await testee.GetRefreshResultAsync(tokens, http),
        };
        await result.ExecuteAsync(http);
        return http;
    }

    [Theory]
    [MemberData(nameof(SignInsThatSetTheMediaCookie))]
    public async Task CookieSignIn_SetsTheMediaCookieForTheProxyRouteUntilTheAccessTokenExpires(
        string kind, SameSiteMode sameSite, string expectedSameSite)
    {
        var testee = new TestFixture(sameSite).CreateTestee();

        var http = await SignInAsync(testee, kind, setCookie: true);

        SetCookiesNamed(http, "media_token").Should().Equal(
            $"media_token=acc_token; expires=Thu, 01 Jan 2026 12:15:00 GMT; path=/api/v1/proxies; secure; samesite={expectedSameSite}; httponly");
    }

    [Theory]
    [InlineData("login")]
    [InlineData("refresh")]
    [InlineData("change-password")]
    public async Task BodySignIn_SetsNoMediaCookie(string kind)
    {
        var testee = new TestFixture().CreateTestee();

        var http = await SignInAsync(testee, kind, setCookie: false);

        http.Response.Headers.SetCookie.Should().BeEmpty();
    }

    // ==========================================================================
    // The user profile
    // ==========================================================================

    public static TheoryData<string, bool> SignInsWithAndWithoutAStorageConfig => new()
    {
        { "register", true },
        { "register", false },
        { "login", true },
        { "login", false },
        { "refresh", true },
        { "refresh", false },
        { "change-password", true },
        { "change-password", false },
    };

    [Theory]
    [MemberData(nameof(SignInsWithAndWithoutAStorageConfig))]
    public async Task SignIn_SaysInTheUserProfileWhetherTheUserHasAStorageConfig(string kind, bool hasStorageConfig)
    {
        var user = new User { Id = Guid.Parse("33333333-3333-3333-3333-333333333333") };
        var fixture = new TestFixture();
        fixture.StorageConfigs.AnyActiveByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(hasStorageConfig);

        var http = await SignInAsync(fixture.CreateTestee(), kind, setCookie: true, user);
        var body = await ReadBodyAsync(http.Response);

        body.Should().Contain($"\"hasStorageConfig\":{(hasStorageConfig ? "true" : "false")}");
        await fixture.StorageConfigs.Received(1).AnyActiveByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusedSignIn_AsksNothingAboutStorageConfigs()
    {
        var fixture = new TestFixture();

        await fixture.CreateTestee().GetLoginResultAsync(new InvalidCredentials(), NewHttp(), setCookie: true);

        await fixture.StorageConfigs.DidNotReceive().AnyActiveByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // ClearSessionCookies
    // ==========================================================================

    [Fact]
    public void ClearSessionCookies_DeletesRefreshTokenCookieFromResponse()
    {
        var http = NewHttp();
        var testee = new TestFixture().CreateTestee();

        testee.ClearSessionCookies(http);

        http.Response.Headers.SetCookie.ToString().Should().Contain("refresh_token");
    }

    [Theory]
    [InlineData(SameSiteMode.Strict, "strict")]
    [InlineData(SameSiteMode.None, "none")]
    public void ClearSessionCookies_DeletesTheMediaCookieWithTheAttributesItWasSetWith(SameSiteMode sameSite, string expectedSameSite)
    {
        var http = NewHttp();
        var testee = new TestFixture(sameSite).CreateTestee();

        testee.ClearSessionCookies(http);

        SetCookiesNamed(http, "media_token").Should().Equal(
            $"media_token=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/api/v1/proxies; secure; samesite={expectedSameSite}; httponly");
    }

    // ==========================================================================
    // GetChangePasswordResult
    // ==========================================================================

    [Fact]
    public async Task GetChangePasswordResult_PasswordTooShort_Returns422WithPolicyTooShortMessage()
    {
        var http = NewHttp();
        var policy = new PasswordPolicy { MinLength = 16 };
        var testee = new TestFixture(passwordPolicy: policy).CreateTestee();

        var result = await testee.GetChangePasswordResultAsync(new PasswordTooShort(), http);
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
        var policy = new PasswordPolicy { MaxLength = 64 };
        var testee = new TestFixture(passwordPolicy: policy).CreateTestee();

        var result = await testee.GetChangePasswordResultAsync(new PasswordTooLong(), http);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(422);
            body.Should().Contain(policy.TooLongMessage);
        });
    }
}
