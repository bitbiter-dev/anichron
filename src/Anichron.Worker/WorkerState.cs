namespace Anichron.Worker;

public sealed class WorkerState
{
    // Written once by WorkerInitializer during startup; safe to read in Worker.ExecuteAsync
    // because IHostedService.StartAsync is called sequentially in registration order.
    public Guid? ResolvedUserId { get; set; }
}
