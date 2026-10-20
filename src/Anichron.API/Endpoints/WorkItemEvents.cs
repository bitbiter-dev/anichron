using Anichron.API.Settings;
using Anichron.Core.Work;
using Microsoft.Extensions.Options;
using NodaTime.Text;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

namespace Anichron.API.Endpoints;

public sealed class WorkItemEvents(IWorkItemReader reader, TimeProvider time, IOptions<WorkItemEventsSettings> settings)
{
    internal const string EventType = "work-item";

    internal static Duration Lookback => Duration.FromSeconds(10);

    /// <summary>
    /// Every work item that changes from the cursor on, re-queried at the configured interval. Each
    /// event's id is the cursor after it, so a reconnect that sends it as Last-Event-ID resumes there.
    /// </summary>
    /// <param name="lastEventId">The cursor to resume from; without a readable one, the latest change.</param>
    /// <param name="ct">Ends the stream.</param>
    /// <returns>One event per changed item; an item may arrive twice, never not at all.</returns>
    public async IAsyncEnumerable<SseItem<WorkItemDto>> StreamAsync(
        string? lastEventId, [EnumeratorCancellation] CancellationToken ct)
    {
        var cursor = Parse(lastEventId) ?? await reader.LatestUpdateAsync(ct) ?? NodaConstants.UnixEpoch;
        var interval = TimeSpan.FromSeconds(settings.Value.IntervalSeconds);
        var sent = new HashSet<(Guid, Instant)>();
        while (!ct.IsCancellationRequested)
        {
            var changed = await reader.UpdatedSinceAsync(cursor - Lookback, ct);
            foreach (var item in changed.Where(item => !sent.Contains((item.Id, item.UpdatedAt))))
            {
                cursor = Instant.Max(cursor, item.UpdatedAt);
                yield return new SseItem<WorkItemDto>(WorkItemDtoMapper.ToDto(item), EventType)
                {
                    EventId = Format(cursor),
                };
            }

            sent = [.. changed.Select(item => (item.Id, item.UpdatedAt))];
            await Task.Delay(interval, time, ct);
        }
    }

    internal static string Format(Instant cursor) => InstantPattern.ExtendedIso.Format(cursor);

    private static Instant? Parse(string? lastEventId)
        => lastEventId is not null && InstantPattern.ExtendedIso.Parse(lastEventId) is { Success: true } parsed
            ? parsed.Value
            : null;
}
