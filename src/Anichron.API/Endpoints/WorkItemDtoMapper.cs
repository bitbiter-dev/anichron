using Anichron.Core.Domain;
using Anichron.Core.Work;
using NodaTime.Text;
using System.Text.Json;

namespace Anichron.API.Endpoints;

internal static class WorkItemDtoMapper
{
    internal static WorkItemDto ToDto(WorkItemView view)
        => new(
            view.Id,
            view.Kind.ToString(),
            view.StorageConfigId,
            view.RequestedBy,
            view.Status.ToString(),
            view.Attempt,
            view.Stalls,
            view.ClaimedBy,
            Format(view.RequestedAt),
            view.StartedAt is { } started ? Format(started) : null,
            view.FinishedAt is { } finished ? Format(finished) : null,
            Format(view.UpdatedAt),
            view.CancelRequestedAt is { } requested ? Format(requested) : null,
            new WorkProgressDto(view.Progress.Done, view.Progress.Total, view.Progress.Note),
            view is { Status: WorkItemStatus.Succeeded or WorkItemStatus.Cancelled, Result: { } result }
                ? JsonSerializer.Deserialize<JsonElement>(result)
                : null,
            view.Status is WorkItemStatus.Cancelled,
            view.Status is WorkItemStatus.Failed or WorkItemStatus.Cancelled ? view.Error : null);

    internal static string Format(Instant instant) => InstantPattern.ExtendedIso.Format(instant);
}
