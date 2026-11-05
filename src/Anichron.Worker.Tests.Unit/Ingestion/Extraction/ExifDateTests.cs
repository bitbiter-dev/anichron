using Anichron.Worker.Ingestion.Extraction;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion.Extraction;

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

    [Theory]
    [InlineData("+13:00", 13 * 3600)]
    [InlineData("-09:30", -34200)]
    [InlineData("+00:00", 0)]
    public void ParseOffset_ExifOffset_ReadsItsSignHoursAndMinutes(string raw, int seconds)
        => ExifDate.ParseOffset(raw).Should().Be(Offset.FromSeconds(seconds));

    [Theory]
    [InlineData("13:00")]
    [InlineData("+25:00")]
    [InlineData("+13")]
    [InlineData("Z")]
    public void ParseOffset_NotAnExifOffset_IsNull(string raw)
        => ExifDate.ParseOffset(raw).Should().BeNull();
}
