using Anichron.Core.Domain;

namespace Anichron.Core.Tests.Unit.Domain;

public sealed class CaptureSourcesTests
{
    [Fact]
    public void Timed_HoldsTheSourcesWithATimeOfDay_AndLeavesOutDateOnly()
        => CaptureSources.Timed.Should().BeEquivalentTo([CaptureSource.Recorded, CaptureSource.RecordedInUtc]);
}
