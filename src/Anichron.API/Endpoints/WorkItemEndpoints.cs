using Anichron.API.Infrastructure;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Anichron.API.Endpoints;

public static class WorkItemEndpoints
{
    internal const int DefaultPageSize = 50;
    internal const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapWorkItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.WorkItems.Group).WithTags("Work items")
                       .RequireAuthorization(AuthPolicies.Admin);
        group.MapGet(string.Empty, ListAsync);
        group.MapGet(ApiPaths.WorkItems.Events, StreamEvents);
        group.MapGet(ApiPaths.WorkItems.ById, GetAsync);
        group.MapPost(ApiPaths.WorkItems.Cancel, CancelAsync);
        return app;
    }

    internal static async Task<Results<Ok<WorkItemPageDto>, ValidationProblem>> ListAsync(
        WorkItemStatus? status,
        WorkKind? kind,
        int? page,
        int? pageSize,
        [FromServices] IWorkItemReader reader,
        CancellationToken ct)
    {
        var number = page ?? 1;
        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
            return Invalid(nameof(pageSize), $"pageSize must be from 1 to {MaxPageSize}.");
        if (number < 1 || number > int.MaxValue / size)
            return Invalid(nameof(page), "page is out of range.");

        var cursor = await reader.LatestUpdateAsync(ct) ?? NodaConstants.UnixEpoch;
        var found = await reader.ListAsync(new WorkItemQuery(status, kind, (number - 1) * size, size), ct);
        return TypedResults.Ok(new WorkItemPageDto(
            [.. found.Items.Select(WorkItemDtoMapper.ToDto)], number, size, found.Total, WorkItemDtoMapper.Format(cursor)));
    }

    internal static async Task<Results<Ok<WorkItemDto>, NotFound>> GetAsync(
        Guid workItemId,
        [FromServices] IWorkItemReader reader,
        CancellationToken ct)
        => await reader.FindAsync(workItemId, ct) is { } item
            ? TypedResults.Ok(WorkItemDtoMapper.ToDto(item))
            : TypedResults.NotFound();

    /// <summary>
    /// Cancels a queued item at once (200), or asks the running item's Worker to cancel it at its
    /// next heartbeat (202). A finished item is a 409 showing the item as it is.
    /// </summary>
    /// <param name="workItemId">The item to cancel.</param>
    /// <param name="canceller">Makes the status-guarded cancel write.</param>
    /// <param name="reader">Reads the item after the cancel.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The item after the cancel, or 404 when there is none.</returns>
    internal static async Task<Results<Ok<WorkItemDto>, Accepted<WorkItemDto>, Conflict<WorkItemDto>, NotFound>> CancelAsync(
        Guid workItemId,
        [FromServices] IWorkItemCanceller canceller,
        [FromServices] IWorkItemReader reader,
        CancellationToken ct)
    {
        var outcome = await canceller.CancelAsync(workItemId, ct);
        if (outcome == CancelOutcome.NotFound || await reader.FindAsync(workItemId, ct) is not { } item)
            return TypedResults.NotFound();

        var dto = WorkItemDtoMapper.ToDto(item);
        return outcome switch
        {
            CancelOutcome.Cancelled => TypedResults.Ok(dto),
            CancelOutcome.Requested => TypedResults.Accepted(ApiPaths.WorkItems.ItemLocation(workItemId), dto),
            _ => TypedResults.Conflict(dto),
        };
    }

    internal static ServerSentEventsResult<WorkItemDto> StreamEvents(
        [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
        [FromServices] WorkItemEvents events,
        CancellationToken ct)
        => TypedResults.ServerSentEvents(events.StreamAsync(lastEventId, ct));

    private static ValidationProblem Invalid(string field, string message)
        => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
