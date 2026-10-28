namespace Anichron.Core.Work;

/// <summary>
/// Runs one kind of work item. Delivery is at least once: each run starts from scratch and
/// converges, and its side effects are safe to repeat. The returned summary, if any, becomes the
/// item's result.
/// </summary>
/// <typeparam name="TPayload">The payload of the kind this handler runs.</typeparam>
public interface IWorkHandler<in TPayload>
    where TPayload : IWorkPayload
{
    Task<object?> RunAsync(TPayload payload, WorkRun run, CancellationToken ct);
}

/// <summary>The run of one claimed item, as its handler sees it.</summary>
/// <param name="ItemId">The work item being run.</param>
/// <param name="Progress">Where the handler reports its progress.</param>
public sealed record WorkRun(Guid ItemId, IWorkProgress Progress);
