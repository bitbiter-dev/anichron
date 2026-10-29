using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.API.Tests.Unit.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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

    private static async Task AssertFieldProblemAsync(
        HttpResponseMessage response, int status, string code, string field, string detail)
    {
        ((int)response.StatusCode).Should().Be(status);
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("status").GetInt32().Should().Be(status);
        problem.GetProperty("detail").GetString().Should().Be(detail);
        var errors = problem.GetProperty("errors").EnumerateObject().ToList();
        errors.Should().ContainSingle().Which.Name.Should().Be(field);
        errors[0].Value.EnumerateArray().Select(e => e.GetString()).Should().Equal(code);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.Clone();
    }

    [Fact]
    public async Task Login_WrongCredentials_Is401InvalidCredentialsAsync()
    {
        LoginFails(AuthResult.Fail<AuthTokens>(AuthError.InvalidCredentials));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, LoginPath, new { usernameOrEmail = "alice", password = "wrong" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("invalid_credentials");
        problem.GetProperty("status").GetInt32().Should().Be(401);
        problem.GetProperty("detail").GetString().Should().Be("The username, email, or password is incorrect.");
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData(LoginMobilePath)]
    public async Task Login_DisabledAccount_Is401AccountDisabledAsync(string path)
    {
        LoginFails(AuthResult.Fail<AuthTokens>(AuthError.AccountDisabled));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, path, new { usernameOrEmail = "alice", password = "SecurePass1!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("account_disabled");
        problem.GetProperty("detail").GetString().Should().Be("This account has been disabled. Please contact an administrator.");
    }

    [Fact]
    public async Task Login_LockedAccount_Is429AccountLockedWithRetryAfterAsync()
    {
        LoginFails(AuthResult.Locked<AuthTokens>(60));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, LoginPath, new { usernameOrEmail = "alice", password = "wrong" });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(60));
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("account_locked");
        problem.GetProperty("status").GetInt32().Should().Be(429);
        problem.GetProperty("detail").GetString().Should().Be("Too many failed login attempts. Please try again in 60 seconds.");
    }

    [Fact]
    public async Task Refresh_NoToken_Is401RefreshTokenRequiredAsync()
    {
        await using var app = await StartAsync();

        using var response = await app.Client.PostAsync(RefreshPath, content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("refresh_token_required");
        problem.GetProperty("detail").GetString().Should().Be("Refresh token is required.");
    }

    [Theory]
    [InlineData(AuthError.TokenInvalid, "refresh_token_invalid", "Refresh token is invalid or has expired.")]
    [InlineData(AuthError.AccountDisabled, "account_disabled", "This account has been disabled. Please contact an administrator.")]
    public async Task Refresh_Rejected_Is401WithItsCodeAsync(AuthError error, string code, string detail)
    {
        RefreshFails(AuthResult.Fail<AuthTokens>(error));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RefreshPath, new { refreshToken = "tok" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("status").GetInt32().Should().Be(401);
        problem.GetProperty("detail").GetString().Should().Be(detail);
    }

    [Fact]
    public async Task Refresh_LockedAccount_Is429AccountLockedWithRetryAfterAsync()
    {
        RefreshFails(AuthResult.Locked<AuthTokens>(1));
        await using var app = await StartAsync();

        using var response = await PostAsync(app, RefreshPath, new { refreshToken = "tok" });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("account_locked");
        problem.GetProperty("detail").GetString().Should().Be("Too many failed login attempts. Please try again in 1 second.");
    }

    [Fact]
    public async Task Login_OverTheRateLimit_Is429RateLimitedWithRetryAfterAsync()
    {
        LoginFails(AuthResult.Fail<AuthTokens>(AuthError.InvalidCredentials));
        await using var app = await StartAsync(rateLimited: true);
        for (var i = 0; i < 10; i++)
            (await PostAsync(app, LoginPath, new { usernameOrEmail = "alice", password = "wrong" })).Dispose();

        using var response = await PostAsync(app, LoginPath, new { usernameOrEmail = "alice", password = "wrong" });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(60));
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("rate_limited");
        problem.GetProperty("status").GetInt32().Should().Be(429);
        problem.GetProperty("detail").GetString().Should().Be("Too many requests. Please try again later.");
    }

    [Fact]
    public async Task Refresh_OverTheRateLimit_Is429RateLimitedWithRetryAfterAsync()
    {
        RefreshFails(AuthResult.Fail<AuthTokens>(AuthError.TokenInvalid));
        await using var app = await StartAsync(rateLimited: true);
        for (var i = 0; i < 5; i++)
            (await PostAsync(app, RefreshPath, new { refreshToken = "tok" })).Dispose();

        using var response = await PostAsync(app, RefreshPath, new { refreshToken = "tok" });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromMinutes(15));
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("rate_limited");
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

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("invite_token_invalid");
        problem.GetProperty("detail").GetString().Should().Be("The invite token is invalid, expired, or has already been used.");
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
