using Anichron.Infrastructure.Configuration;
using System.Net;

namespace Anichron.Infrastructure.Tests.Unit.Configuration;

public sealed class ConfigurationListTests
{
    [Fact]
    public void Parse_CommaSeparatedValues_TrimsAndParsesEachEntry()
    {
        var result = ConfigurationList.Parse(" 10.0.0.1 ,10.0.0.2", IPAddress.Parse);

        result.Should().Equal(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2"));
    }

    [Fact]
    public void Parse_EmptyEntries_AreDropped()
    {
        var result = ConfigurationList.Parse("a,, ,b,", s => s);

        result.Should().Equal("a", "b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_MissingValue_ReturnsEmptyList(string? value)
    {
        var result = ConfigurationList.Parse(value, s => s);

        result.Should().BeEmpty();
    }
}
