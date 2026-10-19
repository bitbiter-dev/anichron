using Anichron.Core.Work;

namespace Anichron.Worker.Work;

internal sealed class WorkProgressSink : IWorkProgress
{
    private WorkProgress snapshot = WorkProgress.None;

    public WorkProgress Snapshot => Volatile.Read(ref snapshot);

    public void Report(long done, long? total = null, string? note = null)
        => Volatile.Write(ref snapshot, new WorkProgress(done, total, note));
}
