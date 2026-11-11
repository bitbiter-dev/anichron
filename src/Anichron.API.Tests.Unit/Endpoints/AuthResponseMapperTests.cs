using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AuthResponseMapperTests
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

        public AuthResponseMapper CreateTestee()
            => new(_cookieSettings, _clock, Options.Create(new UsernamePolicy()), Options.Create(_passwordPolicy));
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

        var result = testee.GetRegistrationResult(
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

        var result = testee.GetLoginResult(
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

        var result = testee.GetLoginResult(
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

        var result = testee.GetRefreshResult(
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

        var result = testee.GetRefreshResult(
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

    private static IResult SignIn(AuthResponseMapper testee, string kind, bool setCookie, out HttpContext http)
    {
        var tokens = new AuthTokens("acc_token", "ref_token", TimeSpan.FromMinutes(15), new User());
        http = NewHttp(withRefreshCookie: setCookie);
        return kind switch
        {
            "register" => testee.GetRegistrationResult(tokens, http),
            "login" => testee.GetLoginResult(tokens, http, setCookie),
            "change-password" => testee.GetChangePasswordResult(tokens, http),
            _ => testee.GetRefreshResult(tokens, http),
        };
    }

    [Theory]
    [MemberData(nameof(SignInsThatSetTheMediaCookie))]
    public async Task CookieSignIn_SetsTheMediaCookieForTheProxyRouteUntilTheAccessTokenExpires(
        string kind, SameSiteMode sameSite, string expectedSameSite)
    {
        var testee = new TestFixture(sameSite).CreateTestee();

        await SignIn(testee, kind, setCookie: true, out var http).ExecuteAsync(http);

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

        await SignIn(testee, kind, setCookie: false, out var http).ExecuteAsync(http);

        http.Response.Headers.SetCookie.Should().BeEmpty();
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

        var result = testee.GetChangePasswordResult(new PasswordTooShort(), http);
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

        var result = testee.GetChangePasswordResult(new PasswordTooLong(), http);
        await result.ExecuteAsync(http);
        var body = await ReadBodyAsync(http.Response);

        Assert.Multiple(() =>
        {
            http.Response.StatusCode.Should().Be(422);
            body.Should().Contain(policy.TooLongMessage);
        });
    }

    // ==========================================================================
    // GetAdminCreateUserResult
    // ==========================================================================

    [Fact]
    public void GetAdminCreateUserResult_Success_Returns201WithLocationAndBody()
    {
        var userId = Guid.NewGuid();
        var created = new AdminCreatedUser(userId, "alice", "alice@example.com", "TempPass==");
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminCreateUserResult(created);

        var ok = result.Should()
            .BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<AdminCreatedUserResponse>>().Subject;
        ok.StatusCode.Should().Be(201);
        ok.Location.Should().Contain(userId.ToString());
        ok.Value.Should().Be(new AdminCreatedUserResponse(userId, "alice", "alice@example.com", "TempPass=="));
    }

    // ==========================================================================
    // GetAdminResetPasswordResult
    // ==========================================================================

    [Fact]
    public void GetAdminResetPasswordResult_NullResult_Returns404()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminResetPasswordResult(null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void GetAdminResetPasswordResult_Success_Returns200WithTemporaryPassword()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminResetPasswordResult(new AdminUserPasswordReset("TempPass=="));

        var ok = result.Should()
            .BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<AdminPasswordResetResponse>>().Subject;
        ok.StatusCode.Should().Be(200);
        ok.Value.Should().Be(new AdminPasswordResetResponse("TempPass=="));
    }

    // ==========================================================================
    // GetAdminGetUsersResult
    // ==========================================================================

    [Fact]
    public void GetAdminGetUsersResult_EmptyList_Returns200WithEmptyCollection()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminGetUsersResult([]);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public void GetAdminGetUsersResult_WithUsers_Returns200WithMappedResponses()
    {
        var testee = new TestFixture().CreateTestee();
        var userId = new Guid("11111111-0000-0000-0000-000000000001");
        var user = new User
        {
            Id = userId,
            Username = "alice",
            Email = "alice@example.com",
            IsAdmin = true,
            IsDisabled = false,
            StorageConfigs = [new() { Id = Guid.NewGuid() }, new() { Id = Guid.NewGuid() }],
        };

        var ok = testee.GetAdminGetUsersResult([user])
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<AdminUserResponse>>>().Subject;

        ok.StatusCode.Should().Be(200);
        ok.Value.Should().ContainSingle(r =>
            r.Id == userId &&
            r.Username == "alice" &&
            r.Email == "alice@example.com" &&
            r.IsAdmin &&
            !r.IsDisabled &&
            r.StorageConfigCount == 2);
    }

    // ==========================================================================
    // GetAdminGetUserResult
    // ==========================================================================

    [Fact]
    public void GetAdminGetUserResult_NullUser_Returns404()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminGetUserResult(null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void GetAdminGetUserResult_UserFound_Returns200WithMappedResponse()
    {
        var testee = new TestFixture().CreateTestee();
        var userId = new Guid("22222222-0000-0000-0000-000000000002");
        var user = new User
        {
            Id = userId,
            Username = "bob",
            Email = "bob@example.com",
            IsAdmin = false,
            IsDisabled = true,
            StorageConfigs = [new() { Id = Guid.NewGuid() }],
        };

        var ok = testee.GetAdminGetUserResult(user)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<AdminUserResponse>>().Subject;

        ok.StatusCode.Should().Be(200);
        ok.Value.Should().Be(new AdminUserResponse(userId, "bob", "bob@example.com", false, true, 1));
    }

    // ==========================================================================
    // GetAdminPatchUserResult
    // ==========================================================================

    [Fact]
    public void GetAdminPatchUserResult_Success_Returns200WithMappedUser()
    {
        var testee = new TestFixture().CreateTestee();
        var userId = new Guid("33333333-0000-0000-0000-000000000003");
        var user = new User
        {
            Id = userId,
            Username = "carol",
            Email = "carol@example.com",
            IsAdmin = true,
            IsDisabled = false,
            StorageConfigs = [],
        };

        var ok = testee.GetAdminPatchUserResult(user)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<AdminUserResponse>>().Subject;

        ok.StatusCode.Should().Be(200);
        ok.Value.Should().Be(new AdminUserResponse(userId, "carol", "carol@example.com", true, false, 0));
    }

    // ==========================================================================
    // GetAdminDeleteUserResult
    // ==========================================================================

    [Fact]
    public void GetAdminDeleteUserResult_Success_Returns204()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminDeleteUserResult(null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    // ==========================================================================
    // GetAdminGetStorageConfigsResult
    // ==========================================================================

    [Fact]
    public void GetAdminGetStorageConfigsResult_Success_ReturnsMappedList()
    {
        var testee = new TestFixture().CreateTestee();
        var userId = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var configId = new Guid("bbbbbbbb-0000-0000-0000-000000000001");
        var configs = new List<UserStorageConfig>
        {
            new() { Id = configId, UserId = userId, RootPath = "/nas/photos", IsActive = true },
        };

        var ok = testee.GetAdminGetStorageConfigsResult(configs)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<AdminStorageConfigResponse>>>().Subject;

        ok.StatusCode.Should().Be(200);
        ok.Value.Should().ContainSingle(c => c == new AdminStorageConfigResponse(configId, userId, "/nas/photos", true));
    }

    [Fact]
    public void GetAdminGetStorageConfigsResult_NoUser_Returns404()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminGetStorageConfigsResult(null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }

    // ==========================================================================
    // GetAdminCreateStorageConfigResult
    // ==========================================================================

    [Fact]
    public void GetAdminCreateStorageConfigResult_Success_Returns201WithLocationAndMappedBody()
    {
        var testee = new TestFixture().CreateTestee();
        var userId = new Guid("aaaaaaaa-0000-0000-0000-000000000002");
        var configId = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        var config = new UserStorageConfig
        {
            Id = configId,
            UserId = userId,
            RootPath = "/nas/photos",
            IsActive = true,
        };

        var created = testee.GetAdminCreateStorageConfigResult(config)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<AdminStorageConfigResponse>>().Subject;

        created.StatusCode.Should().Be(201);
        created.Location.Should().Be($"/api/v1/users/{userId}/storage-configs/{configId}");
        created.Value.Should().Be(new AdminStorageConfigResponse(configId, userId, "/nas/photos", true));
    }

    // ==========================================================================
    // GetAdminDeleteStorageConfigResult
    // ==========================================================================

    [Fact]
    public void GetAdminDeleteStorageConfigResult_Success_Returns204()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminDeleteStorageConfigResult(deleted: true);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    [Fact]
    public void GetAdminDeleteStorageConfigResult_NotDeleted_Returns404()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminDeleteStorageConfigResult(deleted: false);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }
}
