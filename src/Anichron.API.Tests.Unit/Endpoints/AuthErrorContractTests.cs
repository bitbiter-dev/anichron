using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.API.Tests.Unit.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using static Anichron.API.Tests.Unit.Endpoints.ProblemAssertions;

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
                services.AddSingleton<IAuthResponseMapper, AuthResponseMapper>();
            },
            rateLimited);

    private void LoginFails(AuthResult<AuthTokens> result)
        => auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(result);

    private void RefreshFails(AuthResult<AuthTokens> result)
        => auth.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(result);

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
        LoginFails(AuthResult.Fail<AuthTokens>(AuthError.InvalidCredentials));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, path, new { usernameOrEmail = "alice", password = "wrong" });

        await AssertProblemAsync(response, 401, "invalid_credentials", "The username, email, or password is incorrect.");
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData(LoginMobilePath)]
    public async Task Login_DisabledAccount_Is401AccountDisabledAsync(string path)
    {
        LoginFails(AuthResult.Fail<AuthTokens>(AuthError.AccountDisabled));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, path, new { usernameOrEmail = "alice", password = "SecurePass1!" });

        await AssertProblemAsync(response, 401, "account_disabled", "This account has been disabled. Please contact an administrator.");
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData(LoginMobilePath)]
    public async Task Login_LockedAccount_Is429AccountLockedWithRetryAfterAsync(string path)
    {
        LoginFails(AuthResult.Locked<AuthTokens>(60));
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
    [InlineData(AuthError.TokenInvalid, "refresh_token_invalid", "Refresh token is invalid or has expired.")]
    [InlineData(AuthError.AccountDisabled, "account_disabled", "This account has been disabled. Please contact an administrator.")]
    public async Task Refresh_Rejected_Is401WithItsCodeAsync(AuthError error, string code, string detail)
    {
        RefreshFails(AuthResult.Fail<AuthTokens>(error));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RefreshPath, new { refreshToken = "tok" });

        await AssertProblemAsync(response, 401, code, detail);
    }

    [Fact]
    public async Task Refresh_LockedAccount_Is429AccountLockedWithRetryAfterAsync()
    {
        RefreshFails(AuthResult.Locked<AuthTokens>(1));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RefreshPath, new { refreshToken = "tok" });

        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
        await AssertProblemAsync(response, 429, "account_locked", "Too many failed login attempts. Please try again in 1 second.");
    }

    [Fact]
    public async Task Login_OverTheRateLimit_Is429RateLimitedWithRetryAfterAsync()
    {
        LoginFails(AuthResult.Fail<AuthTokens>(AuthError.InvalidCredentials));
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
        RefreshFails(AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid));
        await using var app = await StartAsync(rateLimited: true);
        for (var i = 0; i < 5; i++)
            (await PostAsync(app, RefreshPath, new { refreshToken = "tok" })).Dispose();

        using var response = await PostAsync(app, RefreshPath, new { refreshToken = "tok" });

        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromMinutes(15));
        await AssertProblemAsync(response, 429, "rate_limited", "Too many requests. Please try again later.");
    }

    [Theory]
    [InlineData(AuthError.InvalidUsername, 422, "invalid_username", "username",
        "Username must be 3–32 characters and may only contain letters, digits, underscores, and hyphens.")]
    [InlineData(AuthError.InvalidEmail, 422, "invalid_email", "email", "The provided email address is not valid.")]
    [InlineData(AuthError.UsernameTaken, 409, "username_taken", "username", "Username is already taken.")]
    [InlineData(AuthError.EmailTaken, 409, "email_taken", "email", "An account with this email already exists.")]
    [InlineData(AuthError.PasswordTooShort, 422, "password_too_short", "password", "Password must be at least 12 characters.")]
    [InlineData(AuthError.PasswordTooLong, 422, "password_too_long", "password", "Password must be no longer than 128 characters.")]
    [InlineData(AuthError.PasswordPwned, 422, "password_pwned", "password",
        "This password has appeared in a known data breach. Please choose a different one.")]
    public async Task Register_FieldRejected_IsAValidationProblemOnThatFieldAsync(
        AuthError error, int status, string code, string field, string detail)
    {
        auth.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Fail<AuthTokens>(error));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RegisterPath, RegisterBody);

        await AssertFieldProblemAsync(response, status, code, field, detail);
    }

    [Fact]
    public async Task Register_InvalidInvite_Is422InviteTokenInvalidAsync()
    {
        auth.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Fail<AuthTokens>(AuthError.InviteTokenInvalid));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RegisterPath, RegisterBody);

        var problem = await AssertProblemAsync(
            response, 422, "invite_token_invalid", "The invite token is invalid, expired, or has already been used.");
        problem.TryGetProperty("errors", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(AuthError.InvalidCredentials, 400, "invalid_credentials", "currentPassword",
        "The username, email, or password is incorrect.")]
    [InlineData(AuthError.PasswordTooShort, 422, "password_too_short", "newPassword", "Password must be at least 12 characters.")]
    [InlineData(AuthError.PasswordTooLong, 422, "password_too_long", "newPassword", "Password must be no longer than 128 characters.")]
    [InlineData(AuthError.PasswordPwned, 422, "password_pwned", "newPassword",
        "This password has appeared in a known data breach. Please choose a different one.")]
    public async Task ChangePassword_FieldRejected_IsAValidationProblemOnThatFieldAsync(
        AuthError error, int status, string code, string field, string detail)
    {
        auth.ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Fail(error));
        await using var app = await StartAsync();

        using var response = await ChangePasswordAsync(app);

        await AssertFieldProblemAsync(response, status, code, field, detail);
    }
}
