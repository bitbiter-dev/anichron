using Anichron.API.Endpoints;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Security.Claims;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class ProxySweepEndpointsTests
{
    private static readonly Guid AdminId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid SweepId = Guid.Parse("50000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigA = Guid.Parse("c1000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigB = Guid.Parse("c2000000-0000-0000-0000-000000000000");
    private static readonly Instant Start = Instant.FromUtc(2026, 10, 27, 9, 0);

    private readonly IWorkQueue queue = Substitute.For<IWorkQueue>();
    private readonly IWorkItemReader items = Substitute.For<IWorkItemReader>();
    private readonly IProxyOrphanReader orphans = Substitute.For<IProxyOrphanReader>();
    private readonly List<ProxyDeletionPayload> enqueued = [];

    public ProxySweepEndpointsTests()
    {
        Sweep(WorkKind.ProxySweep, WorkItemStatus.Succeeded);
        queue.EnqueueAsync(Arg.Any<ProxyDeletionPayload>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                enqueued.Add(call.Arg<ProxyDeletionPayload>());
                return Id(900 + enqueued.Count);
            });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClaimsPrincipal Admin => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, AdminId.ToString())], "Test"));

    private static ClaimsPrincipal Nobody => new(new ClaimsIdentity());

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private void Sweep(WorkKind kind, WorkItemStatus status)
        => items.FindAsync(SweepId, Arg.Any<CancellationToken>()).Returns(new WorkItemView(
            SweepId, kind, null, AdminId, Start, status, 1, 0, "all@host", Start, null, Start, WorkProgress.None, null, null, null));

    private void Found(params ProxyOrphanScope[] found)
        => orphans.FindAsync(SweepId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(found);

    private Task<Results<Accepted<ProxyDeletionsRequestedDto>, ValidationProblem, NotFound, Conflict, UnauthorizedHttpResult>> DeleteAsync(
        ProxyDeletionRequest request, ClaimsPrincipal? caller = null)
        => ProxySweepEndpoints.RequestDeletionsAsync(SweepId, request, caller ?? Admin, items, orphans, queue, Ct);

    private Task<Results<Ok<ProxyOrphanPageDto>, ValidationProblem, NotFound, Conflict>> ListAsync(
        int? page = null, int? pageSize = null, ProxyDeletionOutcome? outcome = null, bool? pending = null, Guid? storageConfigId = null)
        => ProxySweepEndpoints.ListOrphansAsync(SweepId, outcome, pending, storageConfigId, page, pageSize, items, orphans, Ct);

    private static void ShouldBeInvalid(object result, string field, string message)
        => result.Should().BeOfType<ValidationProblem>().Which.ProblemDetails.Errors
            .Should().ContainKey(field).WhoseValue.Should().Equal(message);

    // ==========================================================================
    // RequestSweepAsync
    // ==========================================================================

    [Fact]
    public async Task RequestSweepAsync_QueuesASweepForTheCallerAsync()
    {
        queue.EnqueueAsync(new ProxySweepPayload(), AdminId, Arg.Any<CancellationToken>()).Returns(SweepId);

        var result = await ProxySweepEndpoints.RequestSweepAsync(Admin, queue, Ct);

        var accepted = result.Result.Should().BeOfType<Accepted<ProxySweepRequestedDto>>().Subject;
        accepted.Value.Should().Be(new ProxySweepRequestedDto(SweepId));
        accepted.Location.Should().Be($"/api/v1/admin/work-items/{SweepId}");
    }

    [Fact]
    public async Task RequestSweepAsync_CallerWithoutAnId_IsUnauthorizedAsync()
    {
        var result = await ProxySweepEndpoints.RequestSweepAsync(Nobody, queue, Ct);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<ProxySweepPayload>(default!, default, Ct);
    }

    // ==========================================================================
    // ListOrphansAsync
    // ==========================================================================

    [Fact]
    public async Task ListOrphansAsync_MapsThePageOfOrphansAsync()
    {
        orphans.ListAsync(Arg.Any<ProxyOrphanQuery>(), Arg.Any<CancellationToken>()).Returns(new ProxyOrphanPage(
            [
                new ProxyOrphanView(Id(1), "ab/x/thumbnail.jpg", ConfigA, 300, Start, ProxyDeletionOutcome.SkippedChanged, Start + Duration.FromHours(3)),
                new ProxyOrphanView(Id(2), "stray.txt", null, 4, Start, null, null),
            ],
            321));

        var result = await ListAsync();

        var page = result.Result.Should().BeOfType<Ok<ProxyOrphanPageDto>>().Which.Value;
        page.Should().BeEquivalentTo(new ProxyOrphanPageDto(
            [
                new ProxyOrphanDto(Id(1), "ab/x/thumbnail.jpg", ConfigA, 300, "2026-10-27T09:00:00Z", "SkippedChanged", "2026-10-27T12:00:00Z"),
                new ProxyOrphanDto(Id(2), "stray.txt", null, 4, "2026-10-27T09:00:00Z", null, null),
            ],
            1, 100, 321));
    }

    [Fact]
    public async Task ListOrphansAsync_PassesTheFiltersAndThePageAsync()
    {
        orphans.ListAsync(Arg.Any<ProxyOrphanQuery>(), Arg.Any<CancellationToken>()).Returns(new ProxyOrphanPage([], 0));

        await ListAsync(page: 3, pageSize: 20, outcome: ProxyDeletionOutcome.Failed, storageConfigId: ConfigA);

        await orphans.Received(1).ListAsync(
            new ProxyOrphanQuery(SweepId, ProxyDeletionOutcome.Failed, false, ConfigA, Skip: 40, Take: 20), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public async Task ListOrphansAsync_PassesThePendingFilterAsync(bool? pending, bool expected)
    {
        orphans.ListAsync(Arg.Any<ProxyOrphanQuery>(), Arg.Any<CancellationToken>()).Returns(new ProxyOrphanPage([], 0));

        await ListAsync(pending: pending);

        await orphans.Received(1).ListAsync(
            new ProxyOrphanQuery(SweepId, null, expected, null, Skip: 0, Take: 100), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListOrphansAsync_PendingAndAnOutcome_IsInvalidAsync()
        => ShouldBeInvalid(
            (await ListAsync(outcome: ProxyDeletionOutcome.Deleted, pending: true)).Result,
            "pending", "Give either pending or outcome, not both.");

    [Fact]
    public async Task ListOrphansAsync_NotPendingAndAnOutcome_IsReadAsync()
    {
        orphans.ListAsync(Arg.Any<ProxyOrphanQuery>(), Arg.Any<CancellationToken>()).Returns(new ProxyOrphanPage([], 0));

        var result = await ListAsync(outcome: ProxyDeletionOutcome.Deleted, pending: false);

        result.Result.Should().BeOfType<Ok<ProxyOrphanPageDto>>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task ListOrphansAsync_PageSizeInRange_IsReadAsync(int pageSize)
    {
        orphans.ListAsync(Arg.Any<ProxyOrphanQuery>(), Arg.Any<CancellationToken>()).Returns(new ProxyOrphanPage([], 0));

        var result = await ListAsync(pageSize: pageSize);

        result.Result.Should().BeOfType<Ok<ProxyOrphanPageDto>>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task ListOrphansAsync_PageSizeOutOfRange_IsInvalidAsync(int pageSize)
        => ShouldBeInvalid((await ListAsync(pageSize: pageSize)).Result, "pageSize", "pageSize must be from 1 to 1000.");

    [Theory]
    [InlineData(0)]
    [InlineData(21_474_837)]
    public async Task ListOrphansAsync_PageOutOfRange_IsInvalidAsync(int page)
        => ShouldBeInvalid((await ListAsync(page: page)).Result, "page", "page is out of range.");

    [Fact]
    public async Task ListOrphansAsync_LastPageInRange_IsReadAsync()
    {
        orphans.ListAsync(Arg.Any<ProxyOrphanQuery>(), Arg.Any<CancellationToken>()).Returns(new ProxyOrphanPage([], 0));

        var result = await ListAsync(page: 21_474_836);

        result.Result.Should().BeOfType<Ok<ProxyOrphanPageDto>>();
    }

    [Fact]
    public async Task ListOrphansAsync_UnknownSweep_IsNotFoundAsync()
    {
        items.FindAsync(SweepId, Arg.Any<CancellationToken>()).Returns((WorkItemView?)null);

        (await ListAsync()).Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task ListOrphansAsync_WorkItemOfAnotherKind_IsNotFoundAsync()
    {
        Sweep(WorkKind.ProxyDeletion, WorkItemStatus.Succeeded);

        (await ListAsync()).Result.Should().BeOfType<NotFound>();
    }

    [Theory]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Running)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    public async Task ListOrphansAsync_SweepNotSucceeded_IsAConflictAsync(WorkItemStatus status)
    {
        Sweep(WorkKind.ProxySweep, status);

        (await ListAsync()).Result.Should().BeOfType<Conflict>();
        await orphans.DidNotReceiveWithAnyArgs().ListAsync(default!, Ct);
    }

    // ==========================================================================
    // RequestDeletionsAsync: validation
    // ==========================================================================

    [Fact]
    public async Task RequestDeletionsAsync_NeitherIdsNorAll_IsInvalidAsync()
        => ShouldBeInvalid((await DeleteAsync(new ProxyDeletionRequest(null, false))).Result, "OrphanIds", "Give either orphanIds or all, not both.");

    [Fact]
    public async Task RequestDeletionsAsync_BothIdsAndAll_IsInvalidAsync()
        => ShouldBeInvalid((await DeleteAsync(new ProxyDeletionRequest([Id(1)], true))).Result, "OrphanIds", "Give either orphanIds or all, not both.");

    [Fact]
    public async Task RequestDeletionsAsync_NoIds_IsInvalidAsync()
        => ShouldBeInvalid((await DeleteAsync(new ProxyDeletionRequest([], false))).Result, "OrphanIds", "orphanIds must hold from 1 to 1000 ids.");

    [Fact]
    public async Task RequestDeletionsAsync_MoreThanAThousandIds_IsInvalidAsync()
        => ShouldBeInvalid(
            (await DeleteAsync(new ProxyDeletionRequest([.. Enumerable.Range(1, 1001).Select(Id)], false))).Result,
            "OrphanIds", "orphanIds must hold from 1 to 1000 ids.");

    [Fact]
    public async Task RequestDeletionsAsync_AThousandIds_IsQueuedAsync()
    {
        var ids = Enumerable.Range(1, 1000).Select(Id).ToList();
        Found([.. ids.Select(id => new ProxyOrphanScope(id, ConfigA))]);

        var result = await DeleteAsync(new ProxyDeletionRequest(ids, false));

        result.Result.Should().BeOfType<Accepted<ProxyDeletionsRequestedDto>>();
        enqueued.Should().ContainSingle().Which.OrphanIds.Should().HaveCount(1000);
    }

    [Fact]
    public async Task RequestDeletionsAsync_RepeatedIds_CountOnceAsync()
    {
        Found(new ProxyOrphanScope(Id(1), ConfigA));

        var result = await DeleteAsync(new ProxyDeletionRequest([Id(1), Id(1)], false));

        result.Result.Should().BeOfType<Accepted<ProxyDeletionsRequestedDto>>();
        await orphans.Received(1).FindAsync(SweepId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1), Arg.Any<CancellationToken>());
        enqueued.Should().ContainSingle().Which.OrphanIds.Should().Equal(Id(1));
    }

    [Fact]
    public async Task RequestDeletionsAsync_IdNotAnOrphanOfTheSweep_IsInvalidAndQueuesNothingAsync()
    {
        Found(new ProxyOrphanScope(Id(1), ConfigA));

        var result = await DeleteAsync(new ProxyDeletionRequest([Id(1), Id(2)], false));

        ShouldBeInvalid(result.Result, "OrphanIds", "Some ids are not orphans of this sweep.");
        enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestDeletionsAsync_UnknownSweep_IsNotFoundAsync()
    {
        items.FindAsync(SweepId, Arg.Any<CancellationToken>()).Returns((WorkItemView?)null);

        (await DeleteAsync(new ProxyDeletionRequest(null, true))).Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task RequestDeletionsAsync_WorkItemOfAnotherKind_IsNotFoundAsync()
    {
        Sweep(WorkKind.Crawl, WorkItemStatus.Succeeded);

        (await DeleteAsync(new ProxyDeletionRequest(null, true))).Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task RequestDeletionsAsync_SweepNotSucceeded_IsAConflictAsync()
    {
        Sweep(WorkKind.ProxySweep, WorkItemStatus.Running);

        (await DeleteAsync(new ProxyDeletionRequest(null, true))).Result.Should().BeOfType<Conflict>();
        enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestDeletionsAsync_CallerWithoutAnId_IsUnauthorizedAsync()
    {
        (await DeleteAsync(new ProxyDeletionRequest(null, true), Nobody)).Result.Should().BeOfType<UnauthorizedHttpResult>();
        enqueued.Should().BeEmpty();
    }

    // ==========================================================================
    // RequestDeletionsAsync: scoping
    // ==========================================================================

    [Fact]
    public async Task RequestDeletionsAsync_SelectedIds_QueueOneDeletionPerLiveConfigAndOneUnscopedAsync()
    {
        Found(
            new ProxyOrphanScope(Id(1), ConfigB),
            new ProxyOrphanScope(Id(2), null),
            new ProxyOrphanScope(Id(3), ConfigA),
            new ProxyOrphanScope(Id(4), null),
            new ProxyOrphanScope(Id(5), ConfigB));

        var result = await DeleteAsync(new ProxyDeletionRequest([Id(1), Id(2), Id(3), Id(4), Id(5)], false));

        result.Result.Should().BeOfType<Accepted<ProxyDeletionsRequestedDto>>()
            .Which.Value!.WorkItemIds.Should().Equal(Id(901), Id(902), Id(903));
        enqueued.Select(p => (p.SweepId, p.StorageConfigId)).Should().Equal((SweepId, ConfigA), (SweepId, ConfigB), (SweepId, null));
        enqueued.Select(p => p.OrphanIds).Should().BeEquivalentTo(
            [new[] { Id(3) }, [Id(1), Id(5)], [Id(2), Id(4)]], o => o.WithStrictOrdering());
        await queue.Received(3).EnqueueAsync(Arg.Any<ProxyDeletionPayload>(), AdminId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestDeletionsAsync_All_QueuesWholeScopesWithNoSelectionAsync()
    {
        orphans.PendingConfigsAsync(SweepId, Arg.Any<CancellationToken>()).Returns([null, ConfigA]);

        var result = await DeleteAsync(new ProxyDeletionRequest(null, true));

        result.Result.Should().BeOfType<Accepted<ProxyDeletionsRequestedDto>>()
            .Which.Value!.WorkItemIds.Should().Equal(Id(901), Id(902));
        enqueued.Should().Equal(new ProxyDeletionPayload(SweepId, ConfigA, null), new ProxyDeletionPayload(SweepId, null, null));
    }

    [Fact]
    public async Task RequestDeletionsAsync_AllWithNothingPending_QueuesNothingAsync()
    {
        orphans.PendingConfigsAsync(SweepId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await DeleteAsync(new ProxyDeletionRequest(null, true));

        result.Result.Should().BeOfType<Accepted<ProxyDeletionsRequestedDto>>().Which.Value!.WorkItemIds.Should().BeEmpty();
        enqueued.Should().BeEmpty();
    }
}
