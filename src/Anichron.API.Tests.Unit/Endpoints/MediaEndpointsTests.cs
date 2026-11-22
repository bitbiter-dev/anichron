using Anichron.API.Endpoints;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class MediaEndpointsTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AssetId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private sealed class TestFixture
    {
        public IAssetInteractionWriter Interactions { get; } = Substitute.For<IAssetInteractionWriter>();
        public IAssetViewReader Reader { get; } = Substitute.For<IAssetViewReader>();

        public TestFixture()
            => Reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<IAssetSelection>(), Arg.Any<CancellationToken>()).Returns([]);

        public Task<AuthenticatedTestApp> StartAsync()
            => AuthenticatedTestApp.StartAsync(api => api.MapMediaEndpoints(), services =>
            {
                services.AddSingleton(Interactions);
                services.AddSingleton(Reader);
            });
    }

    // ==========================================================================
    // PATCH interaction (#46)
    // ==========================================================================

    private static HttpRequestMessage Patch(object body, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/assets/{AssetId}/interaction")
        {
            Content = JsonContent.Create(body),
        };
        if (userId is { } caller)
            request.Headers.Add(AuthenticatedTestApp.UserHeader, caller.ToString());
        return request;
    }

    [Fact]
    public async Task PatchInteraction_OwnAsset_PassesThePresentFlagsAndReturnsTheResultingStateAsync()
    {
        var fixture = new TestFixture();
        fixture.Interactions.ApplyAsync(UserId, AssetId, new InteractionChange(null, true, null), Arg.Any<CancellationToken>())
            .Returns(new InteractionState(Starred: true, Liked: true, Hidden: false));
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Patch(new { liked = true }, UserId), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AssetInteractionDto>(TestContext.Current.CancellationToken))
            .Should().Be(new AssetInteractionDto(Starred: true, Liked: true, Hidden: false, DisplayWeight: 2.0));
    }

    [Fact]
    public async Task PatchInteraction_EveryFlag_IsPassedAsSentAsync()
    {
        var fixture = new TestFixture();
        fixture.Interactions.ApplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InteractionChange>(), Arg.Any<CancellationToken>())
            .Returns(new InteractionState(false, false, false));
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            Patch(new { starred = true, liked = false, hidden = true }, UserId), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.Interactions.Received(1).ApplyAsync(
            UserId, AssetId, new InteractionChange(true, false, true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchInteraction_EmptyBody_PassesNoChangeAsync()
    {
        var fixture = new TestFixture();
        fixture.Interactions.ApplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InteractionChange>(), Arg.Any<CancellationToken>())
            .Returns(new InteractionState(false, false, false));
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Patch(new { }, UserId), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AssetInteractionDto>(TestContext.Current.CancellationToken))
            .Should().Be(new AssetInteractionDto(false, false, false, 1.0));
        await fixture.Interactions.Received(1).ApplyAsync(
            UserId, AssetId, new InteractionChange(null, null, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchInteraction_NoBodyAtAll_PassesNoChangeAsync()
    {
        var fixture = new TestFixture();
        fixture.Interactions.ApplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InteractionChange>(), Arg.Any<CancellationToken>())
            .Returns(new InteractionState(false, true, false));
        await using var app = await fixture.StartAsync();
        using var client = app.Client;
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/assets/{AssetId}/interaction");
        request.Headers.Add(AuthenticatedTestApp.UserHeader, UserId.ToString());

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AssetInteractionDto>(TestContext.Current.CancellationToken))
            .Should().Be(new AssetInteractionDto(false, true, false, 2.0));
        await fixture.Interactions.Received(1).ApplyAsync(
            UserId, AssetId, new InteractionChange(null, null, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchInteraction_BodyNamingAnotherUser_WritesForTheCallerAsync()
    {
        var fixture = new TestFixture();
        fixture.Interactions.ApplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InteractionChange>(), Arg.Any<CancellationToken>())
            .Returns(new InteractionState(true, false, false));
        await using var app = await fixture.StartAsync();
        using var client = app.Client;
        var other = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

        using var response = await client.SendAsync(
            Patch(new { userId = other, starred = true }, UserId), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.Interactions.Received(1).ApplyAsync(
            UserId, AssetId, new InteractionChange(true, null, null), Arg.Any<CancellationToken>());
        await fixture.Interactions.DidNotReceive().ApplyAsync(
            other, Arg.Any<Guid>(), Arg.Any<InteractionChange>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchInteraction_NotVisibleToTheCaller_ReturnsNotFoundAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Patch(new { starred = true }, UserId), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchInteraction_Anonymous_ReturnsUnauthorizedWithoutAWriteAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Patch(new { starred = true }), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await fixture.Interactions.DidNotReceive().ApplyAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InteractionChange>(), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // GET hidden (#558)
    // ==========================================================================

    private static readonly Instant HiddenOn = Instant.FromUtc(2026, 11, 20, 8, 15, 30) + Duration.FromTicks(1234560);

    private static HttpRequestMessage GetHidden(string query = "")
        => AuthenticatedTestApp.Get($"/api/v1/assets/hidden{query}", UserId);

    private static AssetView Hidden(int n, Instant? hiddenAt = null)
    {
        var view = AssetViews.Image(Guid.Parse($"00000000-0000-0000-0000-{n:D12}"), new LocalDateTime(2019, 11, 12, 9, 0));
        return view with { HiddenAt = hiddenAt ?? (HiddenOn - Duration.FromMinutes(n)) };
    }

    private static AssetView[] HiddenViews(int count) => [.. Enumerable.Range(1, count).Select(n => Hidden(n))];

    private static async Task<HiddenAssetsPageDto> ReadPageAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<HiddenAssetsPageDto>(TestContext.Current.CancellationToken);
        page.Should().NotBeNull();
        return page;
    }

    [Fact]
    public async Task GetHidden_WithoutACursor_ReadsTheCallersFirstPageAndOneMoreAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(GetHidden(), TestContext.Current.CancellationToken);

        var page = await ReadPageAsync(response);
        page.Items.Should().BeEmpty();
        page.Next.Should().BeNull();
        await fixture.Reader.Received(1).ReadAsync(
            UserId, new HiddenAssets(After: null, Take: 61), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetHidden_ReturnsEachAssetAloneWithWhenItWasHiddenAsync()
    {
        var fixture = new TestFixture();
        var view = Hidden(1, HiddenOn) with { BurstId = Guid.NewGuid(), IsStarred = true };
        fixture.Reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<IAssetSelection>(), Arg.Any<CancellationToken>()).Returns([view]);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(GetHidden(), TestContext.Current.CancellationToken);

        var item = (await ReadPageAsync(response)).Items.Should().ContainSingle().Which;
        item.HiddenAt.Should().Be("2026-11-20T08:15:30.123456Z");
        item.Asset.Id.Should().Be(view.Id);
        (item.Asset.Year, item.Asset.Month, item.Asset.Day).Should().Be((2019, 11, 12));
        item.Asset.Burst.Should().BeNull("a hidden frame is shown alone");
        item.Asset.Interaction.Should().Be(new AssetInteractionDto(Starred: true, Liked: false, Hidden: true, DisplayWeight: 1.0));
    }

    [Fact]
    public async Task GetHidden_FullPage_IsTheFirstSixtyWithNoNextAsync()
    {
        var fixture = new TestFixture();
        fixture.Reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<IAssetSelection>(), Arg.Any<CancellationToken>()).Returns(HiddenViews(60));
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(GetHidden(), TestContext.Current.CancellationToken);

        var page = await ReadPageAsync(response);
        page.Items.Should().HaveCount(60);
        page.Next.Should().BeNull();
    }

    [Fact]
    public async Task GetHidden_MoreThanAPage_ReturnsSixtyAndANextCursorThatResumesAfterTheLastAsync()
    {
        var fixture = new TestFixture();
        var views = HiddenViews(61);
        fixture.Reader.ReadAsync(Arg.Any<Guid>(), new HiddenAssets(null, 61), Arg.Any<CancellationToken>()).Returns(views);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var first = await client.SendAsync(GetHidden(), TestContext.Current.CancellationToken);
        var page = await ReadPageAsync(first);
        using var second = await client.SendAsync(
            GetHidden($"?after={Uri.EscapeDataString(page.Next!)}"), TestContext.Current.CancellationToken);

        page.Items.Select(i => i.Asset.Id).Should().Equal(views[..60].Select(v => v.Id));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.Reader.Received(1).ReadAsync(
            UserId, new HiddenAssets(new HiddenCursor(views[59].HiddenAt!.Value, views[59].Id), 61), Arg.Any<CancellationToken>());
    }

    public static TheoryData<string> MalformedCursors { get; } =
    [
        "not a cursor!",
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes("2026-11-20T08:15:30Z")),
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes("2026-11-20T08:15:30Z 00000000000000000000000000000001 x")),
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes("yesterday 00000000000000000000000000000001")),
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes("2026-11-20T08:15:30Z 00000000-0000-0000-0000-000000000001")),
    ];

    [Theory]
    [MemberData(nameof(MalformedCursors))]
    public async Task GetHidden_MalformedCursor_ReturnsBadRequestWithoutReadingAsync(string after)
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            GetHidden($"?after={Uri.EscapeDataString(after)}"), TestContext.Current.CancellationToken);

        await ProblemAssertions.AssertFieldProblemAsync(
            response, 400, "invalid_cursor", "after", "after must be the next cursor of an earlier page.");
        await fixture.Reader.DidNotReceiveWithAnyArgs().ReadAsync(Guid.Empty, null!, CancellationToken.None);
    }

    [Fact]
    public async Task GetHidden_Anonymous_ReturnsUnauthorizedWithoutReadingAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.GetAsync("/api/v1/assets/hidden", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await fixture.Reader.DidNotReceiveWithAnyArgs().ReadAsync(Guid.Empty, null!, CancellationToken.None);
    }
}
