namespace Anichron.Testing.Time;

public abstract record TimelineEvent;

public sealed record Called(string What) : TimelineEvent;

public sealed record TimerArmed(TimeSpan DueTime) : TimelineEvent;

public sealed record TimerFired(TimeSpan DueTime) : TimelineEvent;

public sealed record Finished(TaskStatus Status) : TimelineEvent;
