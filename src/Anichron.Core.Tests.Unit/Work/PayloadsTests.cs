using Anichron.Core.Domain;
using Anichron.Core.Work;

namespace Anichron.Core.Tests.Unit.Work;

public sealed class PayloadsTests
{
    private static (WorkKind Kind, string? DedupKey) Describe<TPayload>(TPayload payload)
        where TPayload : IWorkPayload
        => (TPayload.Kind, payload.DedupKey);

    private static Guid? ScopeOf<TPayload>(TPayload payload)
        where TPayload : IWorkPayload
        => payload.StorageConfigId;

    [Fact]
    public void Crawl_IsOnePeriodicItemPerConfigScopedToIt()
    {
        var configId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        Describe(new CrawlPayload(configId)).Should().Be((WorkKind.Crawl, "crawl:77777777-7777-7777-7777-777777777777"));
        ScopeOf(new CrawlPayload(configId)).Should().Be(configId);
    }

    [Fact]
    public void BurstDetection_IsOnePeriodicItemPerConfigScopedToIt()
    {
        var configId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        Describe(new BurstDetectionPayload(configId)).Should().Be(
            (WorkKind.BurstDetection, "burst-detection:77777777-7777-7777-7777-777777777777"));
        ScopeOf(new BurstDetectionPayload(configId)).Should().Be(configId);
    }

    [Fact]
    public void PerConfigPayloads_AreBuiltForTheirConfig()
    {
        var configId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        (For<CrawlPayload>(configId), For<BurstDetectionPayload>(configId)).Should().Be(
            (new CrawlPayload(configId), new BurstDetectionPayload(configId)));
    }

    private static TPayload For<TPayload>(Guid configId)
        where TPayload : IStorageConfigPayload<TPayload>
        => TPayload.For(configId);

    [Fact]
    public void MaintenancePayloads_AreUnscoped()
        => (ScopeOf(new TokenCleanupPayload()), ScopeOf(new WorkItemRetentionPayload())).Should().Be((null, null));

    [Fact]
    public void TokenCleanup_IsOnePeriodicItemAtATime()
        => Describe(new TokenCleanupPayload()).Should().Be((WorkKind.TokenCleanup, "token-cleanup"));

    [Fact]
    public void WorkItemRetention_IsOnePeriodicItemAtATime()
        => Describe(new WorkItemRetentionPayload()).Should().Be((WorkKind.WorkItemRetention, "work-item-retention"));

    [Fact]
    public void ProxySweep_IsOneUnscopedItemAtATime()
        => (Describe(new ProxySweepPayload()), ScopeOf(new ProxySweepPayload())).Should().Be(((WorkKind.ProxySweep, "proxy-sweep"), null));

    [Fact]
    public void ProxyDeletion_IsScopedToItsConfigAndNeverCoalesced()
    {
        var configId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var deletion = new ProxyDeletionPayload(Guid.NewGuid(), configId, null);

        (Describe(deletion), ScopeOf(deletion)).Should().Be(((WorkKind.ProxyDeletion, null), configId));
    }

    [Fact]
    public void ProxyDeletion_OfNoLiveConfig_IsUnscoped()
        => ScopeOf(new ProxyDeletionPayload(Guid.NewGuid(), null, null)).Should().BeNull();

    [Fact]
    public void ProxyDeletion_RoundTripsWithItsSelection()
    {
        var sweepId = Guid.Parse("50000000-0000-0000-0000-000000000000");
        var orphanId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var read = WorkJson.Deserialize<ProxyDeletionPayload>(WorkJson.Serialize(new ProxyDeletionPayload(sweepId, null, [orphanId])));

        (read.SweepId, read.StorageConfigId).Should().Be((sweepId, null));
        read.OrphanIds.Should().Equal(orphanId);
    }
}
