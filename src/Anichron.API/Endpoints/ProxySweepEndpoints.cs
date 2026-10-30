using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public sealed record ProxySweepRequestedDto(Guid WorkItemId);

public sealed record ProxyDeletionRequest(IReadOnlyList<Guid>? OrphanIds, bool All);

public sealed record ProxyDeletionsRequestedDto(IReadOnlyList<Guid> WorkItemIds);

public sealed record ProxyOrphanDto(
    Guid Id,
    string ProxyPath,
    Guid? StorageConfigId,
    long SizeBytes,
    string LastWriteTime,
    string? Outcome,
    string? ProcessedAt);

public sealed record ProxyOrphanPageDto(IReadOnlyList<ProxyOrphanDto> Items, int Page, int PageSize, int Total);

public static class ProxySweepEndpoints
{
    internal const int DefaultPageSize = 100;
    internal const int MaxPageSize = 1000;
    internal const int MaxSelectedOrphans = 1000;
    internal const string OrphanIdsField = "orphanIds";

    public static IEndpointRouteBuilder MapProxySweepEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.ProxySweeps.Group).WithTags("Proxy sweeps")
                       .RequireAdmin();
        group.MapPost(string.Empty, RequestSweepAsync);
        group.MapGet(ApiPaths.ProxySweeps.Orphans, ListOrphansAsync);
        group.MapPost(ApiPaths.ProxySweeps.Deletions, RequestDeletionsAsync);
        return app;
    }

    /// <summary>Queues a sweep, or returns the one already queued.</summary>
    /// <param name="caller">The requesting admin.</param>
    /// <param name="queue">Takes the request.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>202 with the sweep's work item, whose status and progress the work-item endpoints show.</returns>
    internal static async Task<Results<Accepted<ProxySweepRequestedDto>, UnauthorizedHttpResult>> RequestSweepAsync(
        ClaimsPrincipal caller,
        [FromServices] IWorkQueue queue,
        CancellationToken ct)
    {
        if (!Guid.TryParse(caller.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId))
            return TypedResults.Unauthorized();

        var id = await queue.EnqueueAsync(new ProxySweepPayload(), callerId, ct);
        return TypedResults.Accepted(ApiPaths.WorkItems.ItemLocation(id), new ProxySweepRequestedDto(id));
    }

    internal static async Task<Results<Ok<ProxyOrphanPageDto>, ValidationProblem, NotFound, Conflict>> ListOrphansAsync(
        Guid sweepId,
        ProxyDeletionOutcome? outcome,
        bool? pending,
        Guid? storageConfigId,
        int? page,
        int? pageSize,
        [FromServices] IWorkItemReader items,
        [FromServices] IProxyOrphanReader orphans,
        CancellationToken ct)
    {
        var number = page ?? 1;
        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
            return ApiProblems.InvalidField(ErrorCodes.InvalidPageSize, nameof(pageSize), $"pageSize must be from 1 to {MaxPageSize}.");
        if (number < 1 || number > int.MaxValue / size)
            return ApiProblems.InvalidField(ErrorCodes.InvalidPage, nameof(page), "page is out of range.");
        if (pending is true && outcome is not null)
            return ApiProblems.InvalidField(ErrorCodes.PendingOrOutcome, nameof(pending), "Give either pending or outcome, not both.");

        var sweep = await StateOfAsync(sweepId, items, ct);
        if (sweep == SweepState.Unknown)
            return TypedResults.NotFound();
        if (sweep == SweepState.Unfinished)
            return TypedResults.Conflict();

        var found = await orphans.ListAsync(new ProxyOrphanQuery(sweepId, outcome, pending is true, storageConfigId, (number - 1) * size, size), ct);
        return TypedResults.Ok(new ProxyOrphanPageDto([.. found.Items.Select(ToDto)], number, size, found.Total));
    }

    /// <summary>
    /// Queues one deletion per storage config the selected orphans name; orphans of configs that no
    /// longer exist, or of none, share one unscoped deletion.
    /// </summary>
    /// <param name="sweepId">The sweep whose orphans to delete.</param>
    /// <param name="request">Either the orphans' ids, or every orphan of the sweep.</param>
    /// <param name="caller">The requesting admin.</param>
    /// <param name="items">Reads the sweep's work item.</param>
    /// <param name="orphans">Reads the sweep's orphans.</param>
    /// <param name="queue">Takes the deletions.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>202 with the deletions' work items.</returns>
    internal static async Task<Results<Accepted<ProxyDeletionsRequestedDto>, ValidationProblem, NotFound, Conflict, UnauthorizedHttpResult>> RequestDeletionsAsync(
        Guid sweepId,
        ProxyDeletionRequest request,
        ClaimsPrincipal caller,
        [FromServices] IWorkItemReader items,
        [FromServices] IProxyOrphanReader orphans,
        [FromServices] IWorkQueue queue,
        CancellationToken ct)
    {
        if (!Guid.TryParse(caller.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId))
            return TypedResults.Unauthorized();
        if (request.All == (request.OrphanIds is not null))
            return ApiProblems.InvalidField(ErrorCodes.OrphanIdsOrAll, OrphanIdsField, "Give either orphanIds or all, not both.");
        var selected = request.OrphanIds?.Distinct().ToList();
        if (selected is { Count: 0 or > MaxSelectedOrphans })
            return ApiProblems.InvalidField(ErrorCodes.InvalidOrphanCount, OrphanIdsField, $"orphanIds must hold from 1 to {MaxSelectedOrphans} ids.");

        var sweep = await StateOfAsync(sweepId, items, ct);
        if (sweep == SweepState.Unknown)
            return TypedResults.NotFound();
        if (sweep == SweepState.Unfinished)
            return TypedResults.Conflict();

        IReadOnlyList<ProxyDeletionPayload> deletions;
        if (selected is null)
        {
            deletions = ProxyDeletionPlan.ForAll(sweepId, await orphans.PendingConfigsAsync(sweepId, ct));
        }
        else
        {
            var found = await orphans.FindAsync(sweepId, selected, ct);
            if (found.Count < selected.Count)
                return ApiProblems.InvalidField(ErrorCodes.UnknownOrphans, OrphanIdsField, "Some ids are not orphans of this sweep.");
            deletions = ProxyDeletionPlan.ForSelection(sweepId, found);
        }

        var queued = new List<Guid>();
        foreach (var deletion in deletions)
            queued.Add(await queue.EnqueueAsync(deletion, callerId, ct));

        return TypedResults.Accepted((string?)null, new ProxyDeletionsRequestedDto(queued));
    }

    private enum SweepState
    {
        Unknown = 1,
        Unfinished = 2,
        Succeeded = 3,
    }

    private static async Task<SweepState> StateOfAsync(Guid sweepId, IWorkItemReader items, CancellationToken ct)
        => await items.FindAsync(sweepId, ct) switch
        {
            null or { Kind: not WorkKind.ProxySweep } => SweepState.Unknown,
            { Status: not WorkItemStatus.Succeeded } => SweepState.Unfinished,
            _ => SweepState.Succeeded,
        };

    private static ProxyOrphanDto ToDto(ProxyOrphanView view)
        => new(
            view.Id,
            view.ProxyPath,
            view.StorageConfigId,
            view.SizeBytes,
            WorkItemDtoMapper.Format(view.LastWriteTime),
            view.DeletionOutcome?.ToString(),
            view.ProcessedAt is { } processed ? WorkItemDtoMapper.Format(processed) : null);
}
