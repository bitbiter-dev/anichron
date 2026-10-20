using System.Text.Json;

namespace Anichron.API.Endpoints;

public sealed record WorkItemDto(
    Guid Id,
    string Kind,
    Guid? StorageConfigId,
    Guid? RequestedBy,
    string Status,
    int Attempt,
    int Stalls,
    string? ClaimedBy,
    string RequestedAt,
    string? StartedAt,
    string? FinishedAt,
    string UpdatedAt,
    WorkProgressDto Progress,
    JsonElement? Result,
    string? Error);

public sealed record WorkProgressDto(long Done, long? Total, string? Note);

public sealed record WorkItemPageDto(IReadOnlyList<WorkItemDto> Items, int Page, int PageSize, int Total, string Cursor);
