using Anichron.Core.Domain;
using Anichron.Core.Work;

namespace Anichron.Core.Tests.Unit.Work;

public sealed class PayloadsTests
{
    private static (WorkKind Kind, string? DedupKey) Describe<TPayload>(TPayload payload)
        where TPayload : IWorkPayload
        => (TPayload.Kind, payload.DedupKey);

    [Fact]
    public void TokenCleanup_IsOnePeriodicItemAtATime()
        => Describe(new TokenCleanupPayload()).Should().Be((WorkKind.TokenCleanup, "token-cleanup"));

    [Fact]
    public void WorkItemRetention_IsOnePeriodicItemAtATime()
        => Describe(new WorkItemRetentionPayload()).Should().Be((WorkKind.WorkItemRetention, "work-item-retention"));
}
