using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.API.Tests.Unit.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using static Anichron.API.Tests.Unit.Endpoints.ProblemAssertions;
using static Anichron.API.Tests.Unit.Services.ServiceResults;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class AuthErrorContractTests
{
    private const string LoginPath = "/api/v1/auth/login";
    private const string LoginMobilePath = "/api/v1/auth/login/mobile";
    private const string RefreshPath = "/api/v1/auth/refresh";
    private const string RegisterPath = "/api/v1/auth/register";
    private const string ChangePasswordPath = "/api/v1/users/me/password";

    private readonly IAuthService auth = Substitute.For<IAuthService>();

    private Task<AuthenticatedTestApp> StartAsync(bool rateLimited = false)
        => AuthenticatedTestApp.StartAsync(
            api =>
            {
                api.MapAuthEndpoints();
                api.MapUserEndpoints();
            },
            services =>
            {
                services.AddSingleton(auth);
                services.AddSingleton(new AuthCookieSettings { SameSite = SameSiteMode.Strict, RefreshTokenDays = 7 });
                services.AddSingleton(Substitute.For<IClock>());
                services.AddSingleton<ISessionResponseMapper, SessionResponseMapper>();
            },
            rateLimited);

    private void LoginFails(ILoginError error)
        => auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ServiceResult.Fail<AuthTokens, ILoginError>(error));

    private void RefreshFails(IRefreshError error)
        => auth.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ServiceResult.Fail<AuthTokens, IRefreshError>(error));

    private static readonly object RegisterBody =
        new { username = "alice", email = "alice@example.com", password = "SecurePass1!", inviteToken = "inv" };

    private static async Task<HttpResponseMessage> ChangePasswordAsync(AuthenticatedTestApp app)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ChangePasswordPath)
        {
            Content = JsonContent.Create(new { currentPassword = "OldPass1!", newPassword = "NewPass1!" }),
        };
        request.Headers.Add(AuthenticatedTestApp.UserHeader, Guid.NewGuid().ToString());
        return await app.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> PostAsync(AuthenticatedTestApp app, string path, object body)
        => app.Client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Login_BodyThatIsNotJson_IsAnUnsupportedMediaTypeProblemAsync()
    {
        await using var app = await StartAsync();

        using var response = await app.Client.PostAsync(
            LoginPath, new StringContent("alice"), TestContext.Current.CancellationToken);

        await AssertStatusProblemAsync(response, 415);
    }

    [Fact]
    public async Task Login_MalformedJson_IsABadRequestProblemAsync()
    {
        await using var app = await StartAsync();

        using var response = await app.Client.PostAsync(
            LoginPath, new StringContent("{", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        await AssertStatusProblemAsync(response, 400);
    }

    [Theory]
    [InlineData("/api/v1/auth/password-reset/request")]
    [InlineData("/api/v1/auth/password-reset/confirm")]
    public async Task PasswordReset_IsANotImplementedProblemAsync(string path)
    {
        await using var app = await StartAsync();

        using var response = await app.Client.PostAsync(path, null, TestContext.Current.CancellationToken);

        await AssertStatusProblemAsync(response, 501);
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData(LoginMobilePath)]
    public async Task Login_WrongCredentials_Is401InvalidCredentialsAsync(string path)
    {
        LoginFails(new InvalidCredentials());
        await using var app = await StartAsync();

        using var response = await PostAsync(app, path, new { usernameOrEmail = "alice", password = "wrong" });

        await AssertProblemAsync(response, 401, "invalid_credentials", "The username, email, or password is incorrect.");
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData(LoginMobilePath)]
    public async Task Login_DisabledAccount_Is401AccountDisabledAsync(string path)
    {
        LoginFails(new AccountDisabled());
        await using var app = await StartAsync();

        using var response = await PostAsync(app, path, new { usernameOrEmail = "alice", password = "SecurePass1!" });

        await AssertProblemAsync(response, 401, "account_disabled", "This account has been disabled. Please contact an administrator.");
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData(LoginMobilePath)]
    public async Task Login_LockedAccount_Is429AccountLockedWithRetryAfterAsync(string path)
    {
        LoginFails(new AccountLocked(60));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, path, new { usernameOrEmail = "alice", password = "wrong" });

        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(60));
        await AssertProblemAsync(response, 429, "account_locked", "Too many failed login attempts. Please try again in 60 seconds.");
    }

    [Fact]
    public async Task Refresh_NoToken_Is401RefreshTokenRequiredAsync()
    {
        await using var app = await StartAsync();

        using var response = await app.Client.PostAsync(RefreshPath, content: null, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, 401, "refresh_token_required", "Refresh token is required.");
    }

    [Theory]
    [InlineData(typeof(RefreshTokenInvalid), "refresh_token_invalid", "Refresh token is invalid or has expired.")]
    [InlineData(typeof(AccountDisabled), "account_disabled", "This account has been disabled. Please contact an administrator.")]
    public async Task Refresh_Rejected_Is401WithItsCodeAsync(Type errorType, string code, string detail)
    {
        var error = NewError<IRefreshError>(errorType);
        RefreshFails(error);
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RefreshPath, new { refreshToken = "tok" });

        await AssertProblemAsync(response, 401, code, detail);
    }

    [Fact]
    public async Task Refresh_LockedAccount_Is429AccountLockedWithRetryAfterAsync()
    {
        RefreshFails(new AccountLocked(1));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RefreshPath, new { refreshToken = "tok" });

        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
        await AssertProblemAsync(response, 429, "account_locked", "Too many failed login attempts. Please try again in 1 second.");
    }

    [Fact]
    public async Task Login_OverTheRateLimit_Is429RateLimitedWithRetryAfterAsync()
    {
        LoginFails(new InvalidCredentials());
        await using var app = await StartAsync(rateLimited: true);
        for (var i = 0; i < 10; i++)
            (await PostAsync(app, LoginPath, new { usernameOrEmail = "alice", password = "wrong" })).Dispose();

        using var response = await PostAsync(app, LoginPath, new { usernameOrEmail = "alice", password = "wrong" });

        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(60));
        await AssertProblemAsync(response, 429, "rate_limited", "Too many requests. Please try again later.");
    }

    [Fact]
    public async Task Refresh_OverTheRateLimit_Is429RateLimitedWithRetryAfterAsync()
    {
        RefreshFails(new RefreshTokenInvalid());
        await using var app = await StartAsync(rateLimited: true);
        for (var i = 0; i < 60; i++)
            (await PostAsync(app, RefreshPath, new { refreshToken = "tok" })).Dispose();

        using var response = await PostAsync(app, RefreshPath, new { refreshToken = "tok" });

        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromMinutes(15));
        await AssertProblemAsync(response, 429, "rate_limited", "Too many requests. Please try again later.");
    }

    [Theory]
    [InlineData(typeof(InvalidUsername), 422, "invalid_username", "username", "Username must be 3–32 characters and may only contain letters, digits, underscores, and hyphens.")]
    [InlineData(typeof(InvalidEmail), 422, "invalid_email", "email", "The provided email address is not valid.")]
    [InlineData(typeof(UsernameTaken), 409, "username_taken", "username", "Username is already taken.")]
    [InlineData(typeof(EmailTaken), 409, "email_taken", "email", "An account with this email already exists.")]
    [InlineData(typeof(PasswordTooShort), 422, "password_too_short", "password", "Password must be at least 12 characters.")]
    [InlineData(typeof(PasswordTooLong), 422, "password_too_long", "password", "Password must be no longer than 128 characters.")]
    [InlineData(typeof(PasswordPwned), 422, "password_pwned", "password", "This password has appeared in a known data breach. Please choose a different one.")]
    public async Task Register_FieldRejected_IsAValidationProblemOnThatFieldAsync(
        Type errorType, int status, string code, string field, string detail)
    {
        var error = NewError<IRegisterError>(errorType);
        auth.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ServiceResult.Fail<AuthTokens, IRegisterError>(error));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RegisterPath, RegisterBody);

        await AssertFieldProblemAsync(response, status, code, field, detail);
    }

    [Fact]
    public async Task Register_InvalidInvite_Is422InviteTokenInvalidAsync()
    {
        auth.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InviteTokenInvalid());
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RegisterPath, RegisterBody);

        var problem = await AssertProblemAsync(
            response, 422, "invite_token_invalid", "The invite token is invalid, expired, or has already been used.");
        problem.TryGetProperty("errors", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(CurrentPasswordWrong), 400, "invalid_credentials", "currentPassword", "The username, email, or password is incorrect.")]
    [InlineData(typeof(PasswordTooShort), 422, "password_too_short", "newPassword", "Password must be at least 12 characters.")]
    [InlineData(typeof(PasswordTooLong), 422, "password_too_long", "newPassword", "Password must be no longer than 128 characters.")]
    [InlineData(typeof(PasswordPwned), 422, "password_pwned", "newPassword", "This password has appeared in a known data breach. Please choose a different one.")]
    public async Task ChangePassword_FieldRejected_IsAValidationProblemOnThatFieldAsync(
        Type errorType, int status, string code, string field, string detail)
    {
        var error = NewError<IChangePasswordError>(errorType);
        auth.ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ServiceResult.Fail<AuthTokens, IChangePasswordError>(error));
        await using var app = await StartAsync();

        using var response = await ChangePasswordAsync(app);

        await AssertFieldProblemAsync(response, status, code, field, detail);
    }
}
