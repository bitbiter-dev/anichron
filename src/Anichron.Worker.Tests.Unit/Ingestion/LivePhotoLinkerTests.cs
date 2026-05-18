using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class LivePhotoLinkerTests
{
    private static LivePhotoLinker MakeLinker(MockFileSystem? fileSystem = null)
        => new(fileSystem ?? new MockFileSystem());

    // ==========================================================================
    // Live Photo pairing
    // ==========================================================================

    [Fact]
    public void Link_HeicWithMatchingMovSibling_ReturnsPair()
    {
        var result = MakeLinker().Link(
            ["/nas/IMG_0001.HEIC", "/nas/IMG_0001.MOV"],
            "/nas");

        result.Items.Should().ContainSingle()
            .Which.Should().BeOfType<LivePhotoPairItem>();
    }

    [Fact]
    public void Link_HeicWithMatchingMovSibling_PairContainsCorrectPaths()
    {
        var result = MakeLinker().Link(
            ["/nas/IMG_0001.HEIC", "/nas/IMG_0001.MOV"],
            "/nas");

        var pair = result.Items.Single().Should().BeOfType<LivePhotoPairItem>().Subject;
        pair.AbsolutePath.Should().Be("/nas/IMG_0001.HEIC");
        pair.MovAbsolutePath.Should().Be("/nas/IMG_0001.MOV");
    }

    [Fact]
    public void Link_HeicWithMatchingMovSibling_RelativePathsAreRelativeToRoot()
    {
        var result = MakeLinker().Link(
            ["/nas/2024/IMG_0001.HEIC", "/nas/2024/IMG_0001.MOV"],
            "/nas");

        var pair = result.Items.Single().Should().BeOfType<LivePhotoPairItem>().Subject;
        pair.RelativePath.Should().Be("2024/IMG_0001.HEIC");
        pair.MovRelativePath.Should().Be("2024/IMG_0001.MOV");
    }

    [Fact]
    public void Link_HeicWithMatchingMovSibling_BothPathsClaimed()
    {
        var result = MakeLinker().Link(
            ["/nas/IMG_0001.HEIC", "/nas/IMG_0001.MOV"],
            "/nas");

        result.ClaimedPaths.Should().Contain("/nas/IMG_0001.HEIC");
        result.ClaimedPaths.Should().Contain("/nas/IMG_0001.MOV");
    }

    [Fact]
    public void Link_HeicWithoutMovSibling_ReturnsSingleImageItem()
    {
        var result = MakeLinker().Link(["/nas/photo.HEIC"], "/nas");

        result.Items.Should().ContainSingle()
            .Which.Should().BeOfType<SingleFileItem>()
            .Which.MediaType.Should().Be(MediaType.Image);
    }

    [Fact]
    public void Link_HeicWithoutMovSibling_HeicPathStillClaimed()
    {
        var result = MakeLinker().Link(["/nas/photo.HEIC"], "/nas");

        result.ClaimedPaths.Should().Contain("/nas/photo.HEIC");
    }

    [Fact]
    public void Link_MovWithoutHeicSibling_IsNotReturned()
    {
        var result = MakeLinker().Link(["/nas/video.MOV"], "/nas");

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public void Link_MovWithoutHeicSibling_IsNotClaimed()
    {
        var result = MakeLinker().Link(["/nas/video.MOV"], "/nas");

        result.ClaimedPaths.Should().NotContain("/nas/video.MOV");
    }

    // ==========================================================================
    // Case-insensitive matching
    // ==========================================================================

    [Fact]
    public void Link_LowercaseExtensions_PairsCorrectly()
    {
        var result = MakeLinker().Link(
            ["/nas/IMG_0001.heic", "/nas/IMG_0001.mov"],
            "/nas");

        result.Items.Should().ContainSingle()
            .Which.Should().BeOfType<LivePhotoPairItem>();
    }

    [Fact]
    public void Link_MixedCaseExtensions_PairsCorrectly()
    {
        var result = MakeLinker().Link(
            ["/nas/IMG_0001.HEIC", "/nas/IMG_0001.mov"],
            "/nas");

        result.Items.Should().ContainSingle()
            .Which.Should().BeOfType<LivePhotoPairItem>();
    }

    // ==========================================================================
    // Empty / non-HEIC files
    // ==========================================================================

    [Fact]
    public void Link_EmptyDirectory_ReturnsEmptyResult()
    {
        var result = MakeLinker().Link([], "/nas");

        result.Items.Should().BeEmpty();
        result.ClaimedPaths.Should().BeEmpty();
    }

    [Fact]
    public void Link_OnlyJpgFiles_ReturnsEmptyResultWithNoClaims()
    {
        var result = MakeLinker().Link(
            ["/nas/photo.jpg", "/nas/other.mp4"],
            "/nas");

        result.Items.Should().BeEmpty();
        result.ClaimedPaths.Should().BeEmpty();
    }
}
