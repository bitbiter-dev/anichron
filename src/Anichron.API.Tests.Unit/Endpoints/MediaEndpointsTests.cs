using Anichron.API.Endpoints;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class MediaEndpointsTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AssetId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private sealed class TestFixture
    {
        public IAssetInteractionWriter Interactions { get; } = Substitute.For<IAssetInteractionWriter>();

        public Task<AuthenticatedTestApp> StartAsync()
            => AuthenticatedTestApp.StartAsync(api => api.MapMediaEndpoints(), services => services.AddSingleton(Interactions));
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
}
