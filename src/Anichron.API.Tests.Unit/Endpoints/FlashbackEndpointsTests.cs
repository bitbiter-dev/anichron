using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class FlashbackEndpointsTests
{
    private const string UserHeader = "X-Test-User";
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private WebApplication? app;

        public IAssetViewReader Reader { get; } = Substitute.For<IAssetViewReader>();

        // 2026-07-14 23:30 UTC is already 15 July in Tokyo.
        public IClock Clock { get; } = Substitute.For<IClock>();
        public DateTimeZone Zone { get; set; } = DateTimeZone.Utc;

        public TestFixture()
        {
            Clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 7, 14, 23, 30));
            Reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<IAssetSelection>(), Arg.Any<CancellationToken>())
                .Returns([]);
        }

        public async Task<HttpClient> StartAsync()
        {
            var builder = ApiTestHost.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", null);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(Reader);
            builder.Services.AddSingleton(Clock);
            builder.Services.AddSingleton(Zone);
            app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapApiEndpoints();
            await app.StartAsync(TestContext.Current.CancellationToken);
            return app.GetTestClient();
        }

        public async ValueTask DisposeAsync()
        {
            if (app is not null)
                await app.DisposeAsync();
        }
    }

    private static HttpRequestMessage Get(string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/flashbacks/on-this-day{query}");
        request.Headers.Add(UserHeader, UserId.ToString());
        return request;
    }

    private static AssetView View(int year, bool starred = false) => new(
        Guid.NewGuid(), MediaType.Image, year, 7, 14, new LocalDateTime(year, 7, 14, 9, 0),
        Width: null, Height: null, Orientation: null, DurationInSeconds: null, Blurhash: null,
        HasThumbnail: false, HasPreview: false, HasVideo: false,
        PairedMovId: null, PairedMovHasVideo: false,
        IsStarred: starred, IsLiked: false);

    [Fact]
    public async Task OnThisDay_WithADate_ReadsThatDayForTheCallerAsync()
    {
        await using var fixture = new TestFixture();
        using var client = await fixture.StartAsync();

        using var response = await client.SendAsync(Get("?date=2026-03-09"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.Reader.Received(1).ReadAsync(
            UserId, new OnThisDay(new LocalDate(2026, 3, 9)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("UTC", 2026, 7, 14)]
    [InlineData("Asia/Tokyo", 2026, 7, 15)]
    public async Task OnThisDay_WithoutADate_UsesTodayInTheServersZoneAsync(string zone, int year, int month, int day)
    {
        await using var fixture = new TestFixture { Zone = DateTimeZoneProviders.Tzdb[zone] };
        using var client = await fixture.StartAsync();

        using var response = await client.SendAsync(Get(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.Reader.Received(1).ReadAsync(
            UserId, new OnThisDay(new LocalDate(year, month, day)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnThisDay_ReturnsTheDayAndTheYearGroupsAsync()
    {
        await using var fixture = new TestFixture();
        AssetView[] views = [View(2015), View(2021, starred: true), View(2015, starred: true)];
        fixture.Reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<IAssetSelection>(), Arg.Any<CancellationToken>()).Returns(views);
        using var client = await fixture.StartAsync();

        using var response = await client.SendAsync(Get("?date=2026-07-14"), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<OnThisDayDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body.Date.Should().Be("2026-07-14");
        body.Years.Select(y => (y.Year, y.Assets.Count)).Should().Equal((2021, 1), (2015, 2));
        body.Years[1].Assets[0].Id.Should().Be(views[2].Id);
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("14.07.2026")]
    [InlineData("2025-02-29")]
    [InlineData("")]
    public async Task OnThisDay_InvalidDate_ReturnsBadRequestWithoutReadingAsync(string date)
    {
        await using var fixture = new TestFixture();
        using var client = await fixture.StartAsync();

        using var response = await client.SendAsync(Get($"?date={date}"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("YYYY-MM-DD");
        await fixture.Reader.DidNotReceiveWithAnyArgs().ReadAsync(Guid.Empty, null!, CancellationToken.None);
    }

    [Fact]
    public async Task OnThisDay_Anonymous_ReturnsUnauthorizedAsync()
    {
        await using var fixture = new TestFixture();
        using var client = await fixture.StartAsync();

        using var response = await client.GetAsync("/api/v1/flashbacks/on-this-day", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
