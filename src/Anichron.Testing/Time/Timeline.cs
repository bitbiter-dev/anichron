using Microsoft.Extensions.Time.Testing;

namespace Anichron.Testing.Time;

/// <summary>
/// An ordered record of what time-dependent code did: the timers it armed on <see cref="Clock"/>,
/// the timers that fired, the collaborator calls the test <see cref="Record"/>s, and the end of
/// every <see cref="Watch"/>ed task. A test states what happens next with <see cref="ExpectAsync"/>,
/// which fails at the first event that differs instead of waiting a guessed amount of real time.
/// </summary>
public sealed class Timeline : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly List<TimelineEvent> events = [];
    private readonly List<TaskCompletionSource<TimelineEvent>> slots = [];
    private readonly List<(Task Ended, Func<Task>? Stop)> watched = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeSpan silenceLimit;
    private int cursor;

    public Timeline()
        : this(DefaultSilenceLimit)
    {
    }

    internal Timeline(TimeSpan silenceLimit)
    {
        this.silenceLimit = silenceLimit;
        Clock = new TimelineClock(this);
    }

    public static TimeSpan DefaultSilenceLimit => TimeSpan.FromSeconds(10);

    public FakeTimeProvider Clock { get; }

    public CancellationToken Lifetime => lifetime.Token;

    public IReadOnlyList<TimelineEvent> Unread
    {
        get
        {
            lock (gate)
                return events[cursor..];
        }
    }

    public void Record(string what) => Append(new Called(what));

    public void Watch(Task subject, Func<Task>? stop = null)
    {
        var ended = subject.ContinueWith(
            task => Append(new Finished(task.Status)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        lock (gate)
            watched.Add((ended, stop));
    }

    public async Task ExpectAsync(params TimelineEvent[] expected)
    {
        ArgumentOutOfRangeException.ThrowIfZero(expected.Length);

        for (var position = 0; position < expected.Length; position++)
        {
            var actual = await ReadAsync(expected[position]);
            if (actual != expected[position])
            {
                throw new TimelineMismatchException(
                    $"Expected {expected[position]} as event {position + 1} of {expected.Length}, but the timeline recorded {actual}.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();

        List<(Task Ended, Func<Task>? Stop)> owned;
        lock (gate)
            owned = [.. watched];

        var ends = new List<Task>(owned.Count);
        foreach (var (ended, stop) in owned)
        {
            if (stop is not null)
                await stop();
            ends.Add(ended);
        }

        await Task.WhenAll(ends).WaitAsync(silenceLimit, CancellationToken.None);
        lifetime.Dispose();
    }

    internal void Append(TimelineEvent timelineEvent)
    {
        TaskCompletionSource<TimelineEvent> slot;
        lock (gate)
        {
            slot = SlotAt(events.Count);
            events.Add(timelineEvent);
        }

        slot.SetResult(timelineEvent);
    }

    private async Task<TimelineEvent> ReadAsync(TimelineEvent awaited)
    {
        Task<TimelineEvent> next;
        lock (gate)
        {
            if (cursor < events.Count)
                return events[cursor++];
            next = SlotAt(cursor).Task;
        }

        try
        {
            var read = await next.WaitAsync(silenceLimit, CancellationToken.None);
            lock (gate)
                cursor++;
            return read;
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Expected {awaited}, but the timeline went silent for {silenceLimit}.", ex);
        }
    }

    private TaskCompletionSource<TimelineEvent> SlotAt(int position)
    {
        if (slots.Count == position)
            slots.Add(new TaskCompletionSource<TimelineEvent>(TaskCreationOptions.RunContinuationsAsynchronously));
        return slots[position];
    }
}
