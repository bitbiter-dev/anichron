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
            [.. found.Items.Select(WorkItemDtoMapper.ToDto)], number, size, found.Total, WorkItemEvents.Format(cursor)));
    }

    internal static async Task<Results<Ok<WorkItemDto>, NotFound>> GetAsync(
        Guid workItemId,
        [FromServices] IWorkItemReader reader,
        CancellationToken ct)
        => await reader.FindAsync(workItemId, ct) is { } item
            ? TypedResults.Ok(WorkItemDtoMapper.ToDto(item))
            : TypedResults.NotFound();

    internal static ServerSentEventsResult<WorkItemDto> StreamEvents(
        [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
        [FromServices] WorkItemEvents events,
        CancellationToken ct)
        => TypedResults.ServerSentEvents(events.StreamAsync(lastEventId, ct));

    private static ValidationProblem Invalid(string field, string message)
        => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
