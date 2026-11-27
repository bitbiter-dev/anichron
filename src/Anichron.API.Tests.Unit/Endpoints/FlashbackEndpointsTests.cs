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
    private static readonly Guid BurstId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private sealed class TestFixture
    {
        public IFlashbackReader Reader { get; } = Substitute.For<IFlashbackReader>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public DateTimeZone Zone { get; init; } = DateTimeZone.Utc;

        public TestFixture()
        {
            Clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 7, 14, 23, 30));
            Reader.ReadDayAsync(Arg.Any<Guid>(), Arg.Any<LocalDate>(), Arg.Any<CancellationToken>()).Returns([]);
            Reader.ReadRecentDaysAsync(
                    Arg.Any<Guid>(), Arg.Any<LocalDate>(), Arg.Any<LocalDate?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns([]);
        }

        public Task<AuthenticatedTestApp> StartAsync()
            => AuthenticatedTestApp.StartAsync(api => api.MapFlashbackEndpoints(), services =>
            {
                services.AddSingleton(Reader);
                services.AddSingleton(Clock);
                services.AddSingleton(Zone);
            });
    }

    private static HttpRequestMessage Get(string query = "")
        => AuthenticatedTestApp.Get($"/api/v1/flashbacks/on-this-day{query}", UserId);

    private static HttpRequestMessage GetDays(string query)
        => AuthenticatedTestApp.Get($"/api/v1/flashbacks/days{query}", UserId);

    private static FlashbackMemory Memory(int id, int year, bool starred = false, FlashbackBurst? burst = null)
    {
        var view = AssetViews.Image(Guid.Parse($"00000000-0000-0000-0000-{id:D12}"), new LocalDateTime(year, 7, 14, 9, 0));
        return new(view with { IsStarred = starred, BurstId = burst?.BurstId }, burst);
    }

    // ==========================================================================
    // On This Day
    // ==========================================================================

    [Fact]
    public async Task OnThisDay_WithADate_ReadsThatDayForTheCallerAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get("?date=2026-03-09"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.Reader.Received(1).ReadDayAsync(UserId, new LocalDate(2026, 3, 9), Arg.Any<CancellationToken>());
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
        await fixture.Reader.Received(1).ReadDayAsync(UserId, new LocalDate(year, month, day), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnThisDay_ReturnsTheDayAndTheYearsInTheReadersOrderAsync()
    {
        var fixture = new TestFixture();
        fixture.Reader.ReadDayAsync(Arg.Any<Guid>(), Arg.Any<LocalDate>(), Arg.Any<CancellationToken>()).Returns(
            [new FlashbackYear(2021, [Memory(1, 2021)]), new FlashbackYear(2015, [Memory(3, 2015, starred: true), Memory(2, 2015)])]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get("?date=2026-07-14"), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<OnThisDayDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body.Date.Should().Be("2026-07-14");
        body.Years.Select(y => (y.Year, y.Assets.Count)).Should().Equal((2021, 1), (2015, 2));
        body.Years[1].Assets.Select(a => (a.Id.ToString()[^1..], a.Interaction.Starred)).Should().Equal(("3", true), ("2", false));
        body.Years[1].Assets[0].Burst.Should().BeNull();
    }

    [Fact]
    public async Task OnThisDay_Burst_IsItsCoverCarryingItsBurstAsync()
    {
        var fixture = new TestFixture();
        fixture.Reader.ReadDayAsync(Arg.Any<Guid>(), Arg.Any<LocalDate>(), Arg.Any<CancellationToken>()).Returns(
            [new FlashbackYear(2019, [Memory(1, 2019, burst: new FlashbackBurst(BurstId, 2))])]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get("?date=2026-07-14"), TestContext.Current.CancellationToken);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("\"burst\":{\"burstId\":\"bbbbbbbb-0000-0000-0000-000000000001\",\"isCover\":true,\"count\":2}");
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("14.07.2026")]
    [InlineData("2025-02-29")]
    [InlineData("")]
    public async Task OnThisDay_InvalidDate_ReturnsBadRequestWithoutReadingAsync(string date)
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get($"?date={date}"), TestContext.Current.CancellationToken);

        await ProblemAssertions.AssertFieldProblemAsync(
            response, 400, "invalid_date", "date", "date must be a calendar date in the form YYYY-MM-DD.");
        await fixture.Reader.DidNotReceiveWithAnyArgs().ReadDayAsync(Guid.Empty, default, CancellationToken.None);
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

    // ==========================================================================
    // Recent days (#631)
    // ==========================================================================

    [Fact]
    public async Task Days_ReadsTheWindowForTheCallerAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            GetDays("?before=2026-07-14&since=2026-07-08&limit=6"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.Reader.Received(1).ReadRecentDaysAsync(
            UserId, new LocalDate(2026, 7, 14), new LocalDate(2026, 7, 8), 6, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("366", 366)]
    public async Task Days_WithoutSince_ReadsAYearBackUpToTheLimitAsync(string limit, int read)
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            GetDays($"?before=2026-07-08&limit={limit}"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.Reader.Received(1).ReadRecentDaysAsync(
            UserId, new LocalDate(2026, 7, 8), null, read, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Days_ReturnsEachDayWithItsNewestYearAndThatYearsFirstMemoryAsync()
    {
        var fixture = new TestFixture();
        fixture.Reader.ReadRecentDaysAsync(
                Arg.Any<Guid>(), Arg.Any<LocalDate>(), Arg.Any<LocalDate?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new RecentDay(new LocalDate(2026, 7, 13), 2021, Memory(1, 2021, starred: true)),
                new RecentDay(new LocalDate(2026, 6, 2), 2019, Memory(2, 2019, burst: new FlashbackBurst(BurstId, 3))),
            ]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(GetDays("?before=2026-07-14&limit=6"), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<RecentDaysDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body.Days.Select(d => (d.Date, d.Year, d.Memory.Id.ToString()[^1..], d.Memory.Interaction.Starred, d.Memory.Burst))
            .Should().Equal(
                ("2026-07-13", 2021, "1", true, null),
                ("2026-06-02", 2019, "2", false, new AssetBurstDto(BurstId, IsCover: true, Count: 3)));
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().StartWith("{\"days\":[{\"date\":\"2026-07-13\",\"year\":2021,\"memory\":{");
    }

    [Theory]
    [InlineData("?limit=6", "before")]
    [InlineData("?before=&limit=6", "before")]
    [InlineData("?before=2026-13-01&limit=6", "before")]
    [InlineData("?before=2025-02-29&limit=6", "before")]
    [InlineData("?before=2026-07-14&since=14.07.2026&limit=6", "since")]
    [InlineData("?before=2026-07-14&since=&limit=6", "since")]
    public async Task Days_InvalidDate_ReturnsBadRequestWithoutReadingAsync(string query, string field)
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(GetDays(query), TestContext.Current.CancellationToken);

        await ProblemAssertions.AssertFieldProblemAsync(
            response, 400, "invalid_date", field, $"{field} must be a calendar date in the form YYYY-MM-DD.");
        await fixture.Reader.DidNotReceiveWithAnyArgs().ReadRecentDaysAsync(Guid.Empty, default, null, 0, CancellationToken.None);
    }

    [Theory]
    [InlineData("")]
    [InlineData("&limit=")]
    [InlineData("&limit=0")]
    [InlineData("&limit=367")]
    [InlineData("&limit=-1")]
    [InlineData("&limit=+6")]
    [InlineData("&limit=1.5")]
    [InlineData("&limit=six")]
    [InlineData("&limit=99999999999")]
    public async Task Days_LimitOutOfRange_ReturnsBadRequestWithoutReadingAsync(string limit)
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(GetDays($"?before=2026-07-14{limit}"), TestContext.Current.CancellationToken);

        await ProblemAssertions.AssertFieldProblemAsync(
            response, 400, "invalid_limit", "limit", "limit must be a whole number from 1 to 366.");
        await fixture.Reader.DidNotReceiveWithAnyArgs().ReadRecentDaysAsync(Guid.Empty, default, null, 0, CancellationToken.None);
    }

    [Fact]
    public async Task Days_Anonymous_ReturnsUnauthorizedAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.GetAsync(
            "/api/v1/flashbacks/days?before=2026-07-14&limit=6", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
