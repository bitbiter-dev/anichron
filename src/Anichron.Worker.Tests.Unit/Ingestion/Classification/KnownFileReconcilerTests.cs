using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class KnownFileReconcilerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 23, 9, 0);

    [Fact]
    public void Reconcile_MovedFile_KeepsItsBurst()
    {
        var burstId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var stored = new MediaAsset { Id = Guid.NewGuid(), FilePath = "2024/a.heic", FileName = "a.heic", BurstId = burstId };
        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        var reconciled = new KnownFileReconciler(clock, new DuplicateCopyTally(), new FakeLogger<KnownFileReconciler>())
            .Reconcile(new FileClassification(FileOutcome.Moved, stored), "2025/a.heic");

        reconciled.Should().BeTrue();
        stored.Should().BeEquivalentTo(new { FilePath = "2025/a.heic", BurstId = (Guid?)burstId, LastSeenOnNas = Now });
    }
}
