using Anichron.Worker.Bursts;
using Anichron.Worker.Settings;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Xmp;
using Microsoft.Extensions.Logging.Testing;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Anichron.Worker.Tests.Unit.Bursts;

public sealed class OrbThumbnailMatcherTests
{
    private static readonly double Threshold = new WorkerSettings().BurstSimilarityThreshold;

    private readonly MockFileSystem fileSystem = new();
    private readonly FakeLogger<OrbThumbnailMatcher> logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Fixture(string name)
    {
        using var stream = typeof(OrbThumbnailMatcherTests).Assembly.GetManifestResourceStream($"Fixtures.{name}")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // The maintainer's IMG_4386 and IMG_4387: the same leafy scene 3 s apart, panned about 5%.
    private static byte[] Before => Fixture("burst-4386.jpg");
    private static byte[] After => Fixture("burst-4387.jpg");

    private static byte[] Jpeg(Func<int, int, Random, byte> shade)
    {
        var random = new Random(298);
        using var image = new Image<L8>(400, 300);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
                image[x, y] = new L8(shade(x, y, random));
        }

        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static byte[] Noise => Jpeg((_, _, random) => (byte)random.Next(256));

    private static byte[] Blocks
    {
        get
        {
            var shades = new Random(26);
            var grid = Enumerable.Range(0, 16 * 12).Select(_ => (byte)shades.Next(256)).ToArray();
            return Jpeg((x, y, _) => grid[(y / 25 * 16) + (x / 25)]);
        }
    }

    private static byte[] Uniform => Jpeg((_, _, _) => 128);

    private OrbThumbnailMatcher Matcher() => new(fileSystem, logger);

    private void Store(string thumbnail, byte[] bytes) => fileSystem.AddFile($"/data/proxies/{thumbnail}", new MockFileData(bytes));

    // ==========================================================================
    // The committed fixtures
    // ==========================================================================

    [Theory]
    [InlineData("burst-4386.jpg")]
    [InlineData("burst-4387.jpg")]
    public void Fixture_CarriesNoPhotoMetadata(string name)
        => ImageMetadataReader.ReadMetadata(new MemoryStream(Fixture(name)))
            .Should().NotContain(d => d is ExifDirectoryBase || d is XmpDirectory || d is IccDirectory)
            .And.NotBeEmpty();

    // ==========================================================================
    // Scoring real thumbnails
    // ==========================================================================

    [Fact]
    public async Task ScoreAsync_TheSameSceneSlightlyPanned_ScoresAtOrAboveTheDefaultThresholdAsync()
    {
        Store("01/a/thumbnail.jpg", Before);
        Store("02/b/thumbnail.jpg", After);

        var score = await Matcher().ScoreAsync("01/a/thumbnail.jpg", "02/b/thumbnail.jpg", Ct);

        score.Should().BeGreaterThanOrEqualTo(Threshold).And.BeLessThanOrEqualTo(1);
    }

    [Theory]
    [InlineData("noise")]
    [InlineData("blocks")]
    public async Task ScoreAsync_ClearlyDifferentImages_ScoreBelowTheDefaultThresholdAsync(string other)
    {
        Store("a.jpg", Before);
        Store("b.jpg", other == "noise" ? Noise : Blocks);

        var score = await Matcher().ScoreAsync("a.jpg", "b.jpg", Ct);

        score.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(Threshold);
    }

    [Fact]
    public async Task ScoreAsync_UniformImage_HasNoKeypointsAndScoresZeroAsync()
    {
        Store("a.jpg", Before);
        Store("b.jpg", Uniform);

        (await Matcher().ScoreAsync("a.jpg", "b.jpg", Ct)).Should().Be(0);
    }

    [Fact]
    public void Describe_UniformImage_HasNoKeypoints()
        => OrbThumbnailMatcher.Describe(Uniform).Should().BeEquivalentTo(new OrbThumbnailMatcher.Described([], []));

    [Fact]
    public void Describe_RealThumbnail_KeepsOneDescriptorPerKeypoint()
    {
        var features = OrbThumbnailMatcher.Describe(Before)!;

        features.Points.Should().HaveCount(OrbThumbnailMatcher.Features);
        features.Descriptors.Should().HaveCount(OrbThumbnailMatcher.Features * OrbThumbnailMatcher.DescriptorBytes)
            .And.Contain(b => b != 0);
    }

    // ==========================================================================
    // Unreadable thumbnails
    // ==========================================================================

    [Fact]
    public async Task ScoreAsync_MissingThumbnail_ScoresZeroAndWarnsOnceAsync()
    {
        Store("a.jpg", Before);
        var matcher = Matcher();

        var first = await matcher.ScoreAsync("a.jpg", "gone.jpg", Ct);
        var second = await matcher.ScoreAsync("gone.jpg", "a.jpg", Ct);

        (first, second).Should().Be((0.0, 0.0));
        var warning = logger.Collector.GetSnapshot().Should().ContainSingle().Which;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Be("Could not read thumbnail gone.jpg for burst detection; it matches nothing.");
        warning.Exception.Should().BeOfType<FileNotFoundException>();
    }

    [Fact]
    public async Task ScoreAsync_ThumbnailWithoutReadPermission_ScoresZeroAndWarnsAsync()
    {
        var denied = Substitute.For<IFileSystem>();
        denied.Path.Returns(fileSystem.Path);
        denied.File.ReadAllBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task<byte[]>>(_ => throw new UnauthorizedAccessException("denied"));

        var score = await new OrbThumbnailMatcher(denied, logger).ScoreAsync("a.jpg", "b.jpg", Ct);

        score.Should().Be(0);
        logger.Collector.GetSnapshot().Should().ContainSingle().Which.Exception.Should().BeOfType<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ScoreAsync_BytesThatAreNotAnImage_ScoreZeroAsync()
    {
        Store("a.jpg", Before);
        Store("b.jpg", "not a jpeg"u8.ToArray());

        (await Matcher().ScoreAsync("a.jpg", "b.jpg", Ct)).Should().Be(0);
    }

    // ==========================================================================
    // Describing each thumbnail once
    // ==========================================================================

    [Fact]
    public async Task ScoreAsync_ThumbnailSeenBefore_IsNotReadAgainAsync()
    {
        Store("a.jpg", Before);
        Store("b.jpg", After);
        var matcher = Matcher();
        var first = await matcher.ScoreAsync("a.jpg", "b.jpg", Ct);
        fileSystem.RemoveFile("/data/proxies/a.jpg");
        fileSystem.RemoveFile("/data/proxies/b.jpg");

        var again = await matcher.ScoreAsync("b.jpg", "a.jpg", Ct);

        again.Should().BeGreaterThanOrEqualTo(Threshold);
        first.Should().BeGreaterThanOrEqualTo(Threshold);
        logger.Collector.GetSnapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Forget_ThumbnailsAreReadAgainAfterwardsAsync()
    {
        Store("a.jpg", Before);
        Store("b.jpg", After);
        var matcher = Matcher();
        await matcher.ScoreAsync("a.jpg", "b.jpg", Ct);
        fileSystem.RemoveFile("/data/proxies/a.jpg");

        matcher.Forget();

        (await matcher.ScoreAsync("a.jpg", "b.jpg", Ct)).Should().Be(0);
    }

    // ==========================================================================
    // The score on constructed keypoints
    // ==========================================================================

    // Distinct random descriptors; shared ones match exactly, the rest are far from everything.
    private static OrbThumbnailMatcher.Described Features(int count, int seed, float shift = 0)
    {
        var random = new Random(seed);
        var descriptors = new byte[count * OrbThumbnailMatcher.DescriptorBytes];
        random.NextBytes(descriptors);
        var points = Enumerable.Range(0, count).Select(i => new Point2f((i % 7 * 40) + shift, (i / 7 * 30) + (i % 3 * 7) + shift)).ToArray();
        return new OrbThumbnailMatcher.Described(points, descriptors);
    }

    // b's first `shared` keypoints are a's first ones, moved by one translation.
    private static (OrbThumbnailMatcher.Described A, OrbThumbnailMatcher.Described B) Pair(int countA, int countB, int shared)
    {
        var a = Features(countA, 1);
        var b = Features(countB, 2, shift: 5);
        Array.Copy(a.Descriptors, b.Descriptors, shared * OrbThumbnailMatcher.DescriptorBytes);
        return (a, b);
    }

    [Theory]
    [InlineData(9, 9, 9, 0.0)]
    [InlineData(10, 10, 10, 1.0)]
    [InlineData(9, 20, 9, 0.0)]
    [InlineData(20, 9, 9, 0.0)]
    public void Score_FewerKeypointsThanTheMinimumOnEitherSide_ScoresZero(int countA, int countB, int shared, double expected)
    {
        var (a, b) = Pair(countA, countB, shared);

        OrbThumbnailMatcher.Score(a, b).Should().Be(expected);
    }

    [Theory]
    [InlineData(7, 0.0)]
    [InlineData(8, 0.4)]
    public void Score_FewerGoodMatchesThanTheMinimum_ScoresZero(int shared, double expected)
    {
        var (a, b) = Pair(20, 20, shared);

        OrbThumbnailMatcher.Score(a, b).Should().Be(expected);
    }

    [Theory]
    [InlineData(10, 20)]
    [InlineData(20, 10)]
    public void Score_IsTheShareOfTheSmallerSetsKeypoints(int countA, int countB)
    {
        var (a, b) = Pair(countA, countB, 10);

        OrbThumbnailMatcher.Score(a, b).Should().Be(1.0);
    }

    [Fact]
    public void Score_KeypointsThatDoNotFitTheTransform_AreNotCounted()
    {
        var (a, b) = Pair(20, 20, 12);
        for (var i = 0; i < 2; i++)
            b.Points[i] = new Point2f(b.Points[i].X + 100, b.Points[i].Y - 80);

        OrbThumbnailMatcher.Score(a, b).Should().Be(0.5);
    }

    [Fact]
    public void GoodMatches_KeepsOnlyBestMatchesClearlyBetterThanTheSecond()
    {
        DMatch[][] knn =
        [
            [new DMatch(0, 5, 2.9f), new DMatch(0, 6, 4f)],
            [new DMatch(1, 7, 3f), new DMatch(1, 8, 4f)],
            [new DMatch(2, 9, 1f)],
            [new DMatch(3, 4, 0f), new DMatch(3, 1, 10f)],
        ];

        OrbThumbnailMatcher.GoodMatches(knn).Select(m => (m.QueryIdx, m.TrainIdx)).Should().Equal((0, 5), (3, 4));
    }
}
