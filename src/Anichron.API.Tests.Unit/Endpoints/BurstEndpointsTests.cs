using Anichron.API.Endpoints;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class BurstEndpointsTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid BurstId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private sealed class TestFixture
    {
        public IAssetViewReader Reader { get; } = Substitute.For<IAssetViewReader>();

        public TestFixture()
            => Reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<IAssetSelection>(), Arg.Any<CancellationToken>())
                .Returns([]);

        public Task<AuthenticatedTestApp> StartAsync()
            => AuthenticatedTestApp.StartAsync(api => api.MapBurstEndpoints(), services => services.AddSingleton(Reader));
    }

    private static HttpRequestMessage Get(Guid burstId)
        => AuthenticatedTestApp.Get($"/api/v1/bursts/{burstId}/assets", UserId);

    private static AssetView Member(string id, LocalDateTime captured)
        => AssetViews.Image(Guid.Parse(id), captured) with { BurstId = BurstId };

    [Fact]
    public async Task Members_AreListedOldestFirstWithTheEarliestAsCoverAsync()
    {
        var fixture = new TestFixture();
        AssetView[] members =
        [
            Member("00000000-0000-0000-0000-000000000002", new LocalDateTime(2019, 7, 14, 9, 0, 2)),
            Member("00000000-0000-0000-0000-00000000000b", new LocalDateTime(2019, 7, 14, 9, 0, 0)),
            Member("00000000-0000-0000-0000-00000000000a", new LocalDateTime(2019, 7, 14, 9, 0, 0)),
        ];
        fixture.Reader.ReadAsync(UserId, new BurstMembers(BurstId), Arg.Any<CancellationToken>()).Returns(members);
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get(BurstId), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AssetDto[]>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body.Select(a => (a.Id.ToString()[^1..], a.Burst)).Should().Equal(
            ("a", new AssetBurstDto(BurstId, IsCover: true, Count: 3)),
            ("b", new AssetBurstDto(BurstId, IsCover: false, Count: 3)),
            ("2", new AssetBurstDto(BurstId, IsCover: false, Count: 3)));
        body[0].Should().Be(AssetDtoMapper.ToDto(members[2], new AssetBurstDto(BurstId, true, 3)));
    }

    [Fact]
    public async Task NoVisibleMember_ReturnsNotFoundAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(Get(BurstId), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await fixture.Reader.Received(1).ReadAsync(UserId, new BurstMembers(BurstId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Anonymous_ReturnsUnauthorizedAsync()
    {
        var fixture = new TestFixture();
        await using var app = await fixture.StartAsync();
        using var client = app.Client;

        using var response = await client.GetAsync($"/api/v1/bursts/{BurstId}/assets", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
