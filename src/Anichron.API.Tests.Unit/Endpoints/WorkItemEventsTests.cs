using Anichron.API.Endpoints;
using Anichron.API.Settings;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.Time;
using Microsoft.Extensions.Options;
using NodaTime.Text;

namespace Anichron.API.Tests.Unit.Endpoints;

public sealed class WorkItemEventsTests : IAsyncDisposable
{
    private static readonly Instant Start = Instant.FromUtc(2026, 10, 18, 9, 0);
    private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Timeline timeline = new();
    private readonly IWorkItemReader reader = Substitute.For<IWorkItemReader>();
    private readonly Queue<IReadOnlyList<WorkItemView>> polls = new();
    private Instant? latest = At(100);

    public WorkItemEventsTests()
    {
        reader.LatestUpdateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            timeline.Record("latest");
            return latest;
        });
        reader.UpdatedSinceAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            timeline.Record($"since {Iso(call.Arg<Instant>())}");
            return polls.TryDequeue(out var poll) ? poll : [];
        });
    }

    public ValueTask DisposeAsync() => timeline.DisposeAsync();

    private static Instant At(int seconds) => Start + Duration.FromSeconds(seconds);

    private static string Iso(Instant instant) => InstantPattern.ExtendedIso.Format(instant);

    private static WorkItemView Changed(Guid id, int updatedAt)
        => new(id, WorkKind.Crawl, null, null, Start, WorkItemStatus.Running, 1, 0, "all@host",
            Start, null, At(updatedAt), new WorkProgress(3, null, "3 found"), null, null, null);

    private void Stream(string? lastEventId = null, double intervalSeconds = 2)
    {
        var events = new WorkItemEvents(
            reader, timeline.Clock, Options.Create(new WorkItemEventsSettings { IntervalSeconds = intervalSeconds }));
        timeline.Watch(Task.Run(async () =>
        {
            await foreach (var item in events.StreamAsync(lastEventId, timeline.Lifetime))
                timeline.Record($"{item.EventType} {item.Data.Id:N} {item.Data.UpdatedAt} id {item.EventId}");
        }, CancellationToken.None));
    }

    private static string Event(Guid id, int updatedAt, int cursor)
        => $"work-item {id:N} {Iso(At(updatedAt))} id {Iso(At(cursor))}";

    [Fact]
    public Task StreamAsync_NoLastEventId_StartsFromTheLatestChangeLessTheLookbackAsync()
    {
        Stream();

        return timeline.ExpectAsync(new Called("latest"), new Called($"since {Iso(At(90))}"), new TimerArmed(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public Task StreamAsync_NoItemsYet_StartsFromTheBeginningAsync()
    {
        latest = null;

        Stream();

        return timeline.ExpectAsync(new Called("latest"), new Called("since 1969-12-31T23:59:50Z"));
    }

    [Fact]
    public Task StreamAsync_LastEventId_ResumesFromItWithoutAskingForTheLatestAsync()
    {
        Stream(lastEventId: Iso(At(40)));

        return timeline.ExpectAsync(new Called($"since {Iso(At(30))}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-time")]
    [InlineData("2026-10-18")]
    public Task StreamAsync_UnreadableLastEventId_StartsFromTheLatestChangeAsync(string lastEventId)
    {
        Stream(lastEventId);

        return timeline.ExpectAsync(new Called("latest"), new Called($"since {Iso(At(90))}"));
    }

    [Fact]
    public Task StreamAsync_EmitsEachChangedItemWithTheCursorAsItsIdAsync()
    {
        polls.Enqueue([Changed(A, 101), Changed(B, 104)]);

        Stream();

        return timeline.ExpectAsync(
            new Called("latest"),
            new Called($"since {Iso(At(90))}"),
            new Called(Event(A, 101, cursor: 101)),
            new Called(Event(B, 104, cursor: 104)),
            new TimerArmed(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task StreamAsync_AfterTheInterval_ReQueriesFromTheAdvancedCursorAsync()
    {
        polls.Enqueue([Changed(A, 105)]);
        Stream(intervalSeconds: 7.5);
        await timeline.ExpectAsync(
            new Called("latest"), new Called($"since {Iso(At(90))}"), new Called(Event(A, 105, cursor: 105)),
            new TimerArmed(TimeSpan.FromSeconds(7.5)));

        timeline.Clock.Advance(TimeSpan.FromSeconds(7.5));

        await timeline.ExpectAsync(
            new TimerFired(TimeSpan.FromSeconds(7.5)), new Called($"since {Iso(At(95))}"), new TimerArmed(TimeSpan.FromSeconds(7.5)));
    }

    [Fact]
    public async Task StreamAsync_ItemAlreadySentUnchanged_IsNotSentAgainAsync()
    {
        polls.Enqueue([Changed(A, 105)]);
        polls.Enqueue([Changed(A, 105), Changed(B, 106)]);
        Stream();
        await timeline.ExpectAsync(
            new Called("latest"), new Called($"since {Iso(At(90))}"), new Called(Event(A, 105, cursor: 105)),
            new TimerArmed(TimeSpan.FromSeconds(2)));

        timeline.Clock.Advance(TimeSpan.FromSeconds(2));

        await timeline.ExpectAsync(
            new TimerFired(TimeSpan.FromSeconds(2)), new Called($"since {Iso(At(95))}"),
            new Called(Event(B, 106, cursor: 106)), new TimerArmed(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task StreamAsync_ItemChangedAgain_IsSentAgainAsync()
    {
        polls.Enqueue([Changed(A, 105)]);
        polls.Enqueue([Changed(A, 107)]);
        Stream();
        await timeline.ExpectAsync(
            new Called("latest"), new Called($"since {Iso(At(90))}"), new Called(Event(A, 105, cursor: 105)),
            new TimerArmed(TimeSpan.FromSeconds(2)));

        timeline.Clock.Advance(TimeSpan.FromSeconds(2));

        await timeline.ExpectAsync(
            new TimerFired(TimeSpan.FromSeconds(2)), new Called($"since {Iso(At(95))}"),
            new Called(Event(A, 107, cursor: 107)));
    }

    [Fact]
    public async Task StreamAsync_ChangeCommittedBehindTheCursor_IsStillSentAndKeepsTheCursorAsync()
    {
        polls.Enqueue([Changed(A, 105)]);
        polls.Enqueue([Changed(B, 103)]);
        Stream();
        await timeline.ExpectAsync(
            new Called("latest"), new Called($"since {Iso(At(90))}"), new Called(Event(A, 105, cursor: 105)),
            new TimerArmed(TimeSpan.FromSeconds(2)));

        timeline.Clock.Advance(TimeSpan.FromSeconds(2));

        await timeline.ExpectAsync(
            new TimerFired(TimeSpan.FromSeconds(2)), new Called($"since {Iso(At(95))}"),
            new Called(Event(B, 103, cursor: 105)));
    }

    [Fact]
    public Task StreamAsync_ItemsBehindTheStartingCursor_KeepTheCursorAsync()
    {
        polls.Enqueue([Changed(A, 95)]);

        Stream();

        return timeline.ExpectAsync(
            new Called("latest"), new Called($"since {Iso(At(90))}"), new Called(Event(A, 95, cursor: 100)));
    }
}
