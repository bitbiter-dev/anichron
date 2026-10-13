namespace Anichron.Core.Tests.Unit;

public sealed class StoragePathTests
{
    [Theory]
    [InlineData("/nas/photos", "/nas/photos")]
    [InlineData("/nas/photos/", "/nas/photos")]
    [InlineData("  /nas/photos  ", "/nas/photos")]
    [InlineData("/nas//photos/./2024/..", "/nas/photos")]
    [InlineData("/", "/")]
    public void Canonicalize_AbsolutePath_ReturnsCanonicalForm(string input, string expected)
        => StoragePath.Canonicalize(input).Should().Be(expected);

    [Theory]
    [InlineData("nas/photos")]
    [InlineData("./photos")]
    [InlineData("")]
    [InlineData("   ")]
    public void Canonicalize_NotAbsolute_ReturnsNull(string input)
        => StoragePath.Canonicalize(input).Should().BeNull();
}
