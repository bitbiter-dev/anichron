using Anichron.API.Endpoints;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.API.Tests.Unit.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class TokenResponseTests
{
    private readonly IAuthService auth = Substitute.For<IAuthService>();

    private Task<AuthenticatedTestApp> StartAsync()
        => AuthenticatedTestApp.StartAsync(
            api => api.MapAuthEndpoints(),
            services =>
            {
                services.AddSingleton(auth);
                services.AddSingleton(new AuthCookieSettings { SameSite = SameSiteMode.Strict, RefreshTokenDays = 7 });
                services.AddSingleton(Substitute.For<IClock>());
                services.AddSingleton<IAuthResponseMapper, AuthResponseMapper>();
            });

    [Theory]
    [InlineData("/api/v1/auth/login", /*lang=json,strict*/ """{"accessToken":"the-access"}""")]
    [InlineData("/api/v1/auth/login/mobile", /*lang=json,strict*/ """{"accessToken":"the-access","refreshToken":"the-refresh"}""")]
    public async Task Login_ReturnsTheRefreshTokenOnlyToTheMobileClientAsync(string path, string expected)
    {
        auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Ok(new AuthTokens("the-access", "the-refresh")));
        await using var app = await StartAsync();

        using var response = await app.Client.PostAsJsonAsync(
            path, new { usernameOrEmail = "alice", password = "SecurePass1!" }, TestContext.Current.CancellationToken);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be(expected);
    }
}
