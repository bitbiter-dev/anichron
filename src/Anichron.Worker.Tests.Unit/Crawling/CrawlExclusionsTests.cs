using Anichron.Worker.Crawling;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class CrawlExclusionsTests
{
    private static CrawlExclusions Exclusions(string excludedDirectories = "")
        => new(Options.Create(new WorkerSettings { ExcludedDirectories = excludedDirectories }));

    [Theory]
    [InlineData("@eaDir")]
    [InlineData("@EADIR")]
    [InlineData("#recycle")]
    [InlineData("#Recycle")]
    [InlineData("#snapshot")]
    [InlineData("@tmp")]
    [InlineData("@Recycle")]
    [InlineData("@Recently-Snapshot")]
    [InlineData("$RECYCLE.BIN")]
    [InlineData("System Volume Information")]
    [InlineData("RECYCLER")]
    [InlineData("lost+found")]
    [InlineData("files_trashbin")]
    [InlineData("files_versions")]
    [InlineData(".@__thumb")]
    [InlineData(".hidden")]
    [InlineData(".Trash-1000")]
    [InlineData(".zfs")]
    [InlineData(".stversions")]
    public void IsExcludedDirectory_BuiltInOrDotName_ReturnsTrue(string name)
        => Exclusions().IsExcludedDirectory(name).Should().BeTrue();

    [Theory]
    [InlineData("2024")]
    [InlineData("Summer @ beach")]
    [InlineData("recycle")]
    [InlineData("eaDir")]
    public void IsExcludedDirectory_OrdinaryName_ReturnsFalse(string name)
        => Exclusions().IsExcludedDirectory(name).Should().BeFalse();

    [Theory]
    [InlineData("Photos Library.photoslibrary")]
    [InlineData("iPhoto Library.photolibrary")]
    [InlineData("Catalog Previews.lrdata")]
    [InlineData("Smart Previews.LRDATA")]
    [InlineData("Holiday.fcpbundle")]
    [InlineData("iMovie Library.imovielibrary")]
    public void IsExcludedDirectory_AppLibraryPackage_ReturnsTrue(string name)
        => Exclusions().IsExcludedDirectory(name).Should().BeTrue();

    [Theory]
    [InlineData("photoslibrary")]
    [InlineData("Trip.photoslibrary backup")]
    public void IsExcludedDirectory_NameMerelyContainingAPackageSuffix_ReturnsFalse(string name)
        => Exclusions().IsExcludedDirectory(name).Should().BeFalse();

    [Theory]
    [InlineData("Lightroom Previews")]
    [InlineData("lightroom previews")]
    [InlineData("tmp")]
    public void IsExcludedDirectory_ConfiguredExtra_ReturnsTrue(string name)
        => Exclusions(" Lightroom Previews , ,tmp").IsExcludedDirectory(name).Should().BeTrue();

    [Fact]
    public void IsExcludedDirectory_ConfiguredExtras_KeepBuiltIns()
        => Exclusions("tmp").IsExcludedDirectory("@eaDir").Should().BeTrue();

    [Theory]
    [InlineData("._IMG_0001.HEIC")]
    [InlineData(".DS_Store")]
    public void IsExcludedFile_DotName_ReturnsTrue(string name)
        => CrawlExclusions.IsExcludedFile(name).Should().BeTrue();

    [Theory]
    [InlineData("IMG_0001.HEIC")]
    [InlineData("SYNOPHOTO_THUMB_XL.jpg")]
    public void IsExcludedFile_OrdinaryName_ReturnsFalse(string name)
        => CrawlExclusions.IsExcludedFile(name).Should().BeFalse();
}
