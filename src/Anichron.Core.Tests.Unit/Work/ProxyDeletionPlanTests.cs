using Anichron.Core.Data.Repository;
using Anichron.Core.Work;

namespace Anichron.Core.Tests.Unit.Work;

public sealed class ProxyDeletionPlanTests
{
    private static readonly Guid SweepId = Guid.Parse("50000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ConfigB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void ForAll_QueuesEachConfigOnceByIdThenUnscopedWithNoSelection()
        => ProxyDeletionPlan.ForAll(SweepId, [null, ConfigB, ConfigA, ConfigB]).Should().Equal(
            new ProxyDeletionPayload(SweepId, ConfigA, null),
            new ProxyDeletionPayload(SweepId, ConfigB, null),
            new ProxyDeletionPayload(SweepId, null, null));

    [Fact]
    public void ForSelection_QueuesEachConfigsOrphansByConfigIdThenUnscoped()
    {
        var plan = ProxyDeletionPlan.ForSelection(SweepId,
        [
            new ProxyOrphanScope(Id(1), null),
            new ProxyOrphanScope(Id(2), ConfigB),
            new ProxyOrphanScope(Id(3), ConfigA),
            new ProxyOrphanScope(Id(4), null),
            new ProxyOrphanScope(Id(5), ConfigB),
        ]);

        plan.Select(p => (p.SweepId, p.StorageConfigId)).Should().Equal((SweepId, ConfigA), (SweepId, ConfigB), (SweepId, null));
        plan.Select(p => p.OrphanIds).Should().BeEquivalentTo(
            [new[] { Id(3) }, [Id(2), Id(5)], [Id(1), Id(4)]], o => o.WithStrictOrdering());
    }

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");
}
