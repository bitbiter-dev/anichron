namespace Anichron.Worker.Crawling;

public sealed class WorkerState
{
    // Written once by WorkerInitializer during startup. Read it at claim and schedule time, never
    // in a constructor: hosted services are built before any of them starts.
    public Guid? ResolvedUserId { get; set; }
}
