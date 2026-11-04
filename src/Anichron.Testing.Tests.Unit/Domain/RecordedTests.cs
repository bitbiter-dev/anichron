using Anichron.Core.Domain;
using Anichron.Testing.Domain;
using NodaTime;

namespace Anichron.Testing.Tests.Unit.Domain;

public sealed class RecordedTests
{
    [Fact]
    public void At_IsARecordedClockTimeWithoutOffsetOrZone()
        => Recorded.At(new LocalDateTime(2019, 7, 23, 18, 5)).Should().Be(
            new CaptureDate(new LocalDateTime(2019, 7, 23, 18, 5), null, CaptureSource.Recorded, null));
}
