using Anichron.API.Endpoints;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class FlashbackEndpointsTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private sealed class TestFixture
    {
        public IAssetViewReader Reader { get; } = Substitute.For<IAssetViewReader>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public DateTimeZone Zone { get; init; } = DateTimeZone.Utc;

        public TestFixture()
        {
            Clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 7, 14, 23, 30));
            Reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<IAssetSelection>(), Arg.Any<CancellationToken>())
                .Returns([]);
        }

        public Task<AuthenticatedTestApp> StartAsync()
            => AuthenticatedTestApp.StartAsync(services =>
            {
                services.AddSingleton(Reader);
                services.AddSingleton(Clock);
                services.AddSingleton(Zone);
            });
    }

    private static HttpRequestMessage Get(string query = "")
        => AuthenticatedTestApp.Get($"/api/v1/flashbacks/on-this-day{query}", UserId);

    private static AssetView View(int year, bool starred = false)
        => AssetViews.Image(Guid.NewGuid(), new LocalDateTime(year, 7, 14, 9, 0)) with { IsStarred = starred };

    [Fact]
    public async Task OnThisDay_WithADate_ReadsThatDayForTheCallerAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

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
        var fixture = new TestFixture { Zone = DateTimeZoneProviders.Tzdb[zone] };
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.Reader.Received(1).ReadAsync(
            UserId, new OnThisDay(new LocalDate(year, month, day)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnThisDay_ReturnsTheDayAndTheYearGroupsAsync()
    {
        var fixture = new TestFixture();
        AssetView[] views = [View(2015), View(2021, starred: true), View(2015, starred: true)];
        fixture.Reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<IAssetSelection>(), Arg.Any<CancellationToken>()).Returns(views);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

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
    [InlineData("")]
    public async Task OnThisDay_InvalidDate_ReturnsBadRequestWithoutReadingAsync(string date)
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get($"?date={date}"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("YYYY-MM-DD");
        await fixture.Reader.DidNotReceiveWithAnyArgs().ReadAsync(Guid.Empty, null!, CancellationToken.None);
    }

    [Fact]
    public async Task OnThisDay_TwentyNinthOfFebruaryInANonLeapYear_IsNotADateSoNothingIsReadAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get("?date=2025-02-29"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await fixture.Reader.DidNotReceiveWithAnyArgs().ReadAsync(Guid.Empty, null!, CancellationToken.None);
    }

    [Fact]
    public async Task OnThisDay_Anonymous_ReturnsUnauthorizedAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.GetAsync("/api/v1/flashbacks/on-this-day", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
