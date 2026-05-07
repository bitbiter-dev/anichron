namespace Anichron.API.Tests.Unit;

// Placeholder — replace with real tests. Exercises xUnit, FluentAssertions, and NSubstitute
// to verify the test infrastructure compiles and runs correctly.
public sealed class PlaceholderTest
{
    [Fact]
    public void Placeholder_Always_Passes()
    {
        var sub = Substitute.For<IDisposable>();
        sub.DidNotReceive().Dispose();
        true.Should().BeTrue();
    }
}
