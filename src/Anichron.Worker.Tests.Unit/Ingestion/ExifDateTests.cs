using Anichron.Worker.Ingestion;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class ExifDateTests
{
    [Fact]
    public void Parse_ExifDate_ReadsItWithColonsAsDateSeparators()
        => ExifDate.Parse("2024:07:15 13:22:01").Should().Be(new LocalDateTime(2024, 7, 15, 13, 22, 1));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2024-07-15 13:22:01")]
    [InlineData("    :  :     :  :  ")]
    public void Parse_MissingOrNotAnExifDate_IsNull(string? raw)
        => ExifDate.Parse(raw).Should().BeNull();
}
