using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute.ExceptionExtensions;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class SessionResponseTests
{
    private const string Session =
        """{"accessToken":"the-access","expiresIn":900,"user":{"id":"11111111-1111-1111-1111-111111111111","username":"alice","email":"alice@example.com","isAdmin":true,"mustChangePassword":false}""";

    private static readonly AuthTokens Tokens = new(
        "the-access",
        "the-refresh",
        900,
        new User
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Username = "alice",
            Email = "alice@example.com",
            IsAdmin = true,
        });

    private readonly IAuthService auth = Substitute.For<IAuthService>();

    private Task<AuthenticatedTestApp> StartAsync()
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
            });

    private static Task<string> BodyAsync(HttpResponseMessage response)
        => response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    private static string[] CookieNames(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? [.. cookies.Select(cookie => cookie[..cookie.IndexOf('=', StringComparison.Ordinal)])]
            : [];

    private static HttpRequestMessage WithRefreshCookie(HttpRequestMessage request)
    {
        request.Headers.Add("Cookie", $"{AuthMessages.RefreshTokenCookieName}=the-old-refresh");
        return request;
    }

    private static HttpRequestMessage ChangePassword()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/me/password")
        {
            Content = JsonContent.Create(new { currentPassword = "OldPass1!", newPassword = "NewPass1!" }),
        };
        request.Headers.Add(AuthenticatedTestApp.UserHeader, Guid.NewGuid().ToString());
        return request;
    }

    [Theory]
    [InlineData("/api/v1/auth/login", Session + "}")]
    [InlineData("/api/v1/auth/login/mobile", Session + ""","refreshToken":"the-refresh"}""")]
    public async Task Login_AnswersTheSession_WithTheRefreshTokenOnlyForTheMobileClientAsync(string path, string expected)
    {
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(AuthResult.Ok(Tokens));
        await using var app = await StartAsync();

        using var response = await app.Client.PostAsJsonAsync(
            path, new { usernameOrEmail = "alice", password = "SecurePass1!" }, TestContext.Current.CancellationToken);

        (await BodyAsync(response)).Should().Be(expected);
    }

    [Fact]
    public async Task Register_AnswersTheSession_AndSetsTheSessionCookiesAsync()
    {
        auth.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(Tokens));
        await using var app = await StartAsync();

        using var response = await app.Client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { username = "alice", email = "alice@example.com", password = "SecurePass1!", inviteToken = "inv" },
            TestContext.Current.CancellationToken);

        (await BodyAsync(response)).Should().Be(Session + "}");
        CookieNames(response).Should().BeEquivalentTo("refresh_token", "media_token");
    }

    [Fact]
    public async Task Refresh_WithTheCookieAndNoBody_AnswersTheSession_AndRenewsTheCookiesAsync()
    {
        auth.RefreshAsync("the-old-refresh", Arg.Any<CancellationToken>()).Returns(AuthResult.Ok(Tokens));
        await using var app = await StartAsync();
        using var request = WithRefreshCookie(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh"));

        using var response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);

        (await BodyAsync(response)).Should().Be(Session + "}");
        CookieNames(response).Should().BeEquivalentTo("refresh_token", "media_token");
    }

    [Fact]
    public async Task Refresh_WithTheTokenInTheBody_AnswersTheSessionWithTheNewRefreshTokenAsync()
    {
        auth.RefreshAsync("the-old-refresh", Arg.Any<CancellationToken>()).Returns(AuthResult.Ok(Tokens));
        await using var app = await StartAsync();

        using var response = await app.Client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = "the-old-refresh" }, TestContext.Current.CancellationToken);

        (await BodyAsync(response)).Should().Be(Session + ""","refreshToken":"the-refresh"}""");
        CookieNames(response).Should().BeEmpty();
    }

    [Fact]
    public async Task ChangePassword_WithTheRefreshCookie_AnswersTheSession_AndSetsNewSessionCookiesAsync()
    {
        auth.ChangePasswordAsync(Arg.Any<Guid>(), "OldPass1!", "NewPass1!", Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(Tokens));
        await using var app = await StartAsync();
        using var request = WithRefreshCookie(ChangePassword());

        using var response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);

        ((int)response.StatusCode).Should().Be(200);
        (await BodyAsync(response)).Should().Be(Session + "}");
        CookieNames(response).Should().BeEquivalentTo("refresh_token", "media_token");
    }

    [Fact]
    public async Task ChangePassword_WithoutTheRefreshCookie_AnswersTheSessionWithTheNewRefreshTokenAsync()
    {
        auth.ChangePasswordAsync(Arg.Any<Guid>(), "OldPass1!", "NewPass1!", Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(Tokens));
        await using var app = await StartAsync();
        using var request = ChangePassword();

        using var response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);

        ((int)response.StatusCode).Should().Be(200);
        (await BodyAsync(response)).Should().Be(Session + ""","refreshToken":"the-refresh"}""");
        CookieNames(response).Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_WhileTheDatabaseIsUnreachable_LeavesTheRefreshCookieAloneAsync()
    {
        auth.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused)));
        await using var app = await StartAsync();
        using var request = WithRefreshCookie(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh"));

        using var response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);

        ((int)response.StatusCode).Should().Be(503);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }
}
