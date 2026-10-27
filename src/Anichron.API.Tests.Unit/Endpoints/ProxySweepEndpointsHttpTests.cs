using Anichron.API.Endpoints;
using Anichron.API.Tests.Unit.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class ProxySweepEndpointsHttpTests
{
    private static readonly Guid AdminId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid SweepId = Guid.Parse("50000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigId = Guid.Parse("c1000000-0000-0000-0000-000000000000");
    private static readonly Guid OrphanId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Instant Start = Instant.FromUtc(2026, 10, 27, 9, 0);

    private readonly IWorkQueue queue = Substitute.For<IWorkQueue>();
    private readonly IWorkItemReader items = Substitute.For<IWorkItemReader>();
    private readonly IProxyOrphanReader orphans = Substitute.For<IProxyOrphanReader>();

    public ProxySweepEndpointsHttpTests()
        => items.FindAsync(SweepId, Arg.Any<CancellationToken>()).Returns(new WorkItemView(
            SweepId, WorkKind.ProxySweep, null, AdminId, Start, WorkItemStatus.Succeeded, 1, 0, "all@host", Start, Start, Start,
            WorkProgress.None, null, null, null));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<AuthenticatedTestApp> StartAsync()
        => AuthenticatedTestApp.StartAsync(api => api.MapProxySweepEndpoints(), services =>
        {
            services.AddSingleton(queue);
            services.AddSingleton(items);
            services.AddSingleton(orphans);
        });

    private static HttpRequestMessage PostAsAdmin(string path, string? json = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(AuthenticatedTestApp.UserHeader, AdminId.ToString());
        request.Headers.Add(AuthenticatedTestApp.AdminHeader, "true");
        return request;
    }

    [Fact]
    public async Task RequestSweep_AcceptsWithTheWorkItemAsync()
    {
        queue.EnqueueAsync(new ProxySweepPayload(), AdminId, Arg.Any<CancellationToken>()).Returns(SweepId);
        await using var app = await StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(PostAsAdmin("/api/v1/admin/proxy-sweeps"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location!.ToString().Should().Be($"/api/v1/admin/work-items/{SweepId}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("workItemId").GetGuid().Should().Be(SweepId);
    }

    [Fact]
    public async Task RequestSweep_NonAdmin_IsForbiddenAsync()
    {
        await using var app = await StartAsync();
        using var client = app.Client;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/proxy-sweeps");
        request.Headers.Add(AuthenticatedTestApp.UserHeader, AdminId.ToString());

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<ProxySweepPayload>(default!, default, Ct);
    }

    [Fact]
    public async Task ListOrphans_BindsTheFiltersAndPagingFromTheQueryAsync()
    {
        orphans.ListAsync(Arg.Any<ProxyOrphanQuery>(), Arg.Any<CancellationToken>()).Returns(new ProxyOrphanPage(
            [new ProxyOrphanView(OrphanId, "ab/x/thumbnail.jpg", ConfigId, 300, Start, ProxyDeletionOutcome.Deleted, Start)], 11));
        await using var app = await StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(AuthenticatedTestApp.GetAsAdmin(
            $"/api/v1/admin/proxy-sweeps/{SweepId}/orphans?outcome=Deleted&pending=false&storageConfigId={ConfigId}&page=2&pageSize=10", AdminId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await orphans.Received(1).ListAsync(
            new ProxyOrphanQuery(SweepId, ProxyDeletionOutcome.Deleted, false, ConfigId, Skip: 10, Take: 10), Arg.Any<CancellationToken>());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("total").GetInt32().Should().Be(11);
        body.RootElement.GetProperty("items")[0].GetProperty("outcome").GetString().Should().Be("Deleted");
    }

    [Fact]
    public async Task RequestDeletions_BindsAllFromTheBodyAsync()
    {
        orphans.PendingConfigsAsync(SweepId, Arg.Any<CancellationToken>()).Returns([null]);
        queue.EnqueueAsync(new ProxyDeletionPayload(SweepId, null, null), AdminId, Arg.Any<CancellationToken>()).Returns(OrphanId);
        await using var app = await StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            PostAsAdmin($"/api/v1/admin/proxy-sweeps/{SweepId}/deletions", /*lang=json,strict*/ """{"all": true}"""), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("workItemIds")[0].GetGuid().Should().Be(OrphanId);
    }

    [Fact]
    public async Task RequestDeletions_BindsTheOrphanIdsFromTheBodyAsync()
    {
        orphans.FindAsync(SweepId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ProxyOrphanScope(OrphanId, null)]);
        await using var app = await StartAsync();
        using var client = app.Client;

        using var response = await client.SendAsync(
            PostAsAdmin($"/api/v1/admin/proxy-sweeps/{SweepId}/deletions", $$"""{"orphanIds": ["{{OrphanId}}"]}"""), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await queue.Received(1).EnqueueAsync(
            Arg.Is<ProxyDeletionPayload>(p => p.StorageConfigId == null && p.OrphanIds!.SequenceEqual(new[] { OrphanId })),
            AdminId, Arg.Any<CancellationToken>());
    }
}
