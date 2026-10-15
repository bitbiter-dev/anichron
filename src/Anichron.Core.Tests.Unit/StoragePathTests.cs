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

    [Fact]
    public void OriginalsRoot_IsTheSharedContainerMountPoint()
        => StoragePath.OriginalsRoot.Should().Be("/data/originals");

    [Theory]
    [InlineData("/data/originals/mira")]
    [InlineData("/data/originals/mira/2024")]
    public void IsInsideOriginals_Descendant_ReturnsTrue(string canonicalPath)
        => StoragePath.IsInsideOriginals(canonicalPath).Should().BeTrue();

    [Theory]
    [InlineData("/data/originals")]
    [InlineData("/")]
    [InlineData("/data")]
    [InlineData("/data/originals2")]
    [InlineData("/volume1/homes/Mira/Photos")]
    [InlineData("/data/Originals/mira")]
    public void IsInsideOriginals_RootItselfOrOutside_ReturnsFalse(string canonicalPath)
        => StoragePath.IsInsideOriginals(canonicalPath).Should().BeFalse();

    [Theory]
    [InlineData("/data/originals/mira", "/data/originals/mira")]
    [InlineData("/data/originals", "/data/originals/mira")]
    [InlineData("/data/originals/mira", "/data/originals")]
    [InlineData("/data/originals/mira", "/data/originals/mira/2024")]
    public void Overlaps_SameOrNested_ReturnsTrue(string first, string second)
        => StoragePath.Overlaps(first, second).Should().BeTrue();

    [Theory]
    [InlineData("/data/originals/mira", "/data/originals/anna")]
    [InlineData("/data/originals/mira", "/data/originals/mira2")]
    [InlineData("/data/originals/mira2", "/data/originals/mira")]
    [InlineData("/data/originals/Mira", "/data/originals/mira")]
    public void Overlaps_Disjoint_ReturnsFalse(string first, string second)
        => StoragePath.Overlaps(first, second).Should().BeFalse();

    [Fact]
    public void FindOverlap_NestedRootAmongOthers_ReturnsIt()
        => StoragePath.FindOverlap(["/data/originals/anna", "/data/originals/mira/2024"], "/data/originals/mira")
            .Should().Be("/data/originals/mira/2024");

    [Fact]
    public void FindOverlap_OnlyDisjointRoots_ReturnsNull()
        => StoragePath.FindOverlap(["/data/originals/anna", "/data/originals/mira2"], "/data/originals/mira")
            .Should().BeNull();
}
