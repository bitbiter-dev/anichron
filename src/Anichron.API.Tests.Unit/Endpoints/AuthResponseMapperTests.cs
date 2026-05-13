using Anichron.API.Endpoints;
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
    [InlineData(AuthError.InviteTokenInvalid, 422)]
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

        http.Response.Headers.SetCookie.ToString().Should().Contain("refresh_token");
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
            body.Should().Contain("The username, email, or password is incorrect.");
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
            body.Should().Contain("This password has appeared in a known data breach. Please choose a different one.");
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

    // ==========================================================================
    // GetAdminCreateUserResult
    // ==========================================================================

    [Fact]
    public void GetAdminCreateUserResult_Success_Returns201WithLocationAndBody()
    {
        var userId = Guid.NewGuid();
        var created = new AdminCreatedUser(userId, "alice", "alice@example.com", "TempPass==");
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminCreateUserResult(AuthResult.Ok(created));

        var ok = result.Should()
            .BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<AdminCreatedUserResponse>>().Subject;
        ok.StatusCode.Should().Be(201);
        ok.Location.Should().Contain(userId.ToString());
        ok.Value.Should().Be(new AdminCreatedUserResponse(userId, "alice", "alice@example.com", "TempPass=="));
    }

    [Theory]
    [InlineData(AuthError.UsernameTaken, 409)]
    [InlineData(AuthError.EmailTaken, 409)]
    public void GetAdminCreateUserResult_Failure_ReturnsExpectedStatusCode(AuthError error, int expectedStatus)
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminCreateUserResult(AuthResult.Fail<AdminCreatedUser>(error));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public void GetAdminCreateUserResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminCreateUserResult(AuthResult.Fail<AdminCreatedUser>(AuthError.InvalidCredentials));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetAdminCreateUserResult_UndefinedAuthError_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminCreateUserResult(AuthResult.Fail<AdminCreatedUser>((AuthError)999));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
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
        var user = new Core.Domain.User
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
        var user = new Core.Domain.User
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
        var user = new Core.Domain.User
        {
            Id = userId,
            Username = "carol",
            Email = "carol@example.com",
            IsAdmin = true,
            IsDisabled = false,
            StorageConfigs = [],
        };

        var ok = testee.GetAdminPatchUserResult(AuthResult.Ok(user))
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<AdminUserResponse>>().Subject;

        ok.StatusCode.Should().Be(200);
        ok.Value.Should().Be(new AdminUserResponse(userId, "carol", "carol@example.com", true, false, 0));
    }

    [Fact]
    public void GetAdminPatchUserResult_UserNotFound_Returns404()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminPatchUserResult(AuthResult.Fail<Core.Domain.User>(AuthError.UserNotFound));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void GetAdminPatchUserResult_CannotModifySelf_Returns403()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminPatchUserResult(AuthResult.Fail<Core.Domain.User>(AuthError.CannotModifySelf));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public void GetAdminPatchUserResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminPatchUserResult(AuthResult.Fail<Core.Domain.User>(AuthError.InvalidCredentials));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetAdminPatchUserResult_UndefinedAuthError_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminPatchUserResult(AuthResult.Fail<Core.Domain.User>((AuthError)999));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    // ==========================================================================
    // GetAdminDeleteUserResult
    // ==========================================================================

    [Fact]
    public void GetAdminDeleteUserResult_Success_Returns204()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminDeleteUserResult(AuthResult.Ok());

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    [Fact]
    public void GetAdminDeleteUserResult_UserNotFound_Returns404()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminDeleteUserResult(AuthResult.Fail(AuthError.UserNotFound));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void GetAdminDeleteUserResult_CannotModifySelf_Returns403()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminDeleteUserResult(AuthResult.Fail(AuthError.CannotModifySelf));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public void GetAdminDeleteUserResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminDeleteUserResult(AuthResult.Fail(AuthError.InvalidCredentials));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetAdminDeleteUserResult_UndefinedAuthError_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminDeleteUserResult(AuthResult.Fail((AuthError)999));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    // ==========================================================================
    // GetAdminGetStorageConfigsResult
    // ==========================================================================

    [Fact]
    public void GetAdminGetStorageConfigsResult_Success_Returns200WithList()
    {
        var testee = new TestFixture().CreateTestee();
        var configs = new List<Core.Domain.UserStorageConfig>();

        var result = testee.GetAdminGetStorageConfigsResult(AuthResult.Ok(configs));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public void GetAdminGetStorageConfigsResult_UserNotFound_Returns404()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminGetStorageConfigsResult(
            AuthResult.Fail<List<Core.Domain.UserStorageConfig>>(AuthError.UserNotFound));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void GetAdminGetStorageConfigsResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminGetStorageConfigsResult(
            AuthResult.Fail<List<Core.Domain.UserStorageConfig>>(AuthError.InvalidCredentials));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    // ==========================================================================
    // GetAdminCreateStorageConfigResult
    // ==========================================================================

    [Fact]
    public void GetAdminCreateStorageConfigResult_Success_Returns201WithLocation()
    {
        var testee = new TestFixture().CreateTestee();
        var userId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var config = new Core.Domain.UserStorageConfig
        {
            Id = configId,
            UserId = userId,
            RootPath = "/nas/photos",
            IsActive = true,
        };

        var result = testee.GetAdminCreateStorageConfigResult(AuthResult.Ok(config));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(201);
    }

    [Fact]
    public void GetAdminCreateStorageConfigResult_UserNotFound_Returns404()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminCreateStorageConfigResult(
            AuthResult.Fail<Core.Domain.UserStorageConfig>(AuthError.UserNotFound));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void GetAdminCreateStorageConfigResult_PathAlreadyAssigned_Returns409()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminCreateStorageConfigResult(
            AuthResult.Fail<Core.Domain.UserStorageConfig>(AuthError.PathAlreadyAssigned));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public void GetAdminCreateStorageConfigResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminCreateStorageConfigResult(
            AuthResult.Fail<Core.Domain.UserStorageConfig>(AuthError.InvalidCredentials));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    // ==========================================================================
    // GetAdminDeleteStorageConfigResult
    // ==========================================================================

    [Fact]
    public void GetAdminDeleteStorageConfigResult_Success_Returns204()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminDeleteStorageConfigResult(AuthResult.Ok());

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(204);
    }

    [Fact]
    public void GetAdminDeleteStorageConfigResult_StorageConfigNotFound_Returns404()
    {
        var testee = new TestFixture().CreateTestee();

        var result = testee.GetAdminDeleteStorageConfigResult(AuthResult.Fail(AuthError.StorageConfigNotFound));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public void GetAdminDeleteStorageConfigResult_ErrorFromOtherContext_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminDeleteStorageConfigResult(AuthResult.Fail(AuthError.InvalidCredentials));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }

    [Fact]
    public void GetAdminDeleteStorageConfigResult_UndefinedAuthError_ThrowsUnreachableException()
    {
        var testee = new TestFixture().CreateTestee();

        var act = () => testee.GetAdminDeleteStorageConfigResult(AuthResult.Fail((AuthError)999));

        act.Should().Throw<System.Diagnostics.UnreachableException>();
    }
}
