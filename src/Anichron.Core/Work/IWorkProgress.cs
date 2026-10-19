namespace Anichron.Core.Work;

public interface IWorkProgress
{
    /// <summary>Replaces the snapshot.</summary>
    /// <param name="done">Units of work finished so far.</param>
    /// <param name="total">Units of work in all, or null while that is unknown.</param>
    /// <param name="note">A short description of the current step.</param>
    void Report(long done, long? total = null, string? note = null);
}

public sealed record WorkProgress(long Done, long? Total, string? Note)
{
    public static WorkProgress None { get; } = new(0, null, null);
}
