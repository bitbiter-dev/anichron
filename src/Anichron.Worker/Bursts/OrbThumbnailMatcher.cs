using Anichron.Core;
using OpenCvSharp;
using System.IO.Abstractions;

namespace Anichron.Worker.Bursts;

/// <summary>ORB keypoints, a ratio test and a RANSAC homography, as measured on #296 (ADR 0009).</summary>
/// <param name="fileSystem">Reads the stored thumbnails.</param>
/// <param name="logger">Reports unreadable thumbnails.</param>
internal sealed partial class OrbThumbnailMatcher(IFileSystem fileSystem, ILogger<OrbThumbnailMatcher> logger)
    : IThumbnailMatcher
{
    internal const int Features = 500;
    internal const int DescriptorBytes = 32;
    internal const int MinimumKeypoints = 10;
    internal const double Ratio = 0.75;
    internal const int MinimumGoodMatches = 8;
    internal const double RansacThresholdPixels = 5.0;

    private readonly Dictionary<string, Described?> described = [];

    /// <summary>A thumbnail's ORB keypoints, kept as managed copies so nothing native outlives a score.</summary>
    /// <param name="Points">Each keypoint's position.</param>
    /// <param name="Descriptors">Each keypoint's descriptor, <see cref="DescriptorBytes"/> bytes per point, in point order.</param>
    internal sealed record Described(Point2f[] Points, byte[] Descriptors);

    public async Task<double> ScoreAsync(string thumbnailA, string thumbnailB, CancellationToken ct)
        => await DescribeAsync(thumbnailA, ct) is { } a && await DescribeAsync(thumbnailB, ct) is { } b
            ? Score(a, b)
            : 0;

    public void Forget() => described.Clear();

    private async Task<Described?> DescribeAsync(string thumbnail, CancellationToken ct)
    {
        if (described.TryGetValue(thumbnail, out var known))
            return known;

        Described? features;
        try
        {
            features = Describe(await fileSystem.File.ReadAllBytesAsync(
                fileSystem.Path.Combine(StoragePath.ProxiesRoot, thumbnail), ct));
        }
        catch (IOException ex)
        {
            Log.Unreadable(logger, thumbnail, ex);
            features = null;
        }

        described[thumbnail] = features;
        return features;
    }

    /// <summary>The thumbnail's keypoints, or null when the bytes are not an image.</summary>
    /// <param name="encoded">The encoded thumbnail.</param>
    internal static Described? Describe(byte[] encoded)
    {
        using var gray = Cv2.ImDecode(encoded, ImreadModes.Grayscale);
        if (gray.Empty())
            return null;

        using var orb = ORB.Create(Features);
        using var descriptors = new Mat();
        orb.DetectAndCompute(gray, null, out var keypoints, descriptors);
        descriptors.GetArray(out byte[] bytes);
        return new Described([.. keypoints.Select(k => k.Pt)], bytes);
    }

    internal static double Score(Described a, Described b)
    {
        if (a.Points.Length < MinimumKeypoints || b.Points.Length < MinimumKeypoints)
            return 0;

        using var descriptorsA = Mat.FromPixelData(a.Points.Length, DescriptorBytes, MatType.CV_8UC1, a.Descriptors);
        using var descriptorsB = Mat.FromPixelData(b.Points.Length, DescriptorBytes, MatType.CV_8UC1, b.Descriptors);
        using var matcher = new BFMatcher(NormTypes.Hamming);
        var good = GoodMatches(matcher.KnnMatch(descriptorsA, descriptorsB, 2));
        if (good.Count < MinimumGoodMatches)
            return 0;

        using var inliers = new Mat();
        using var homography = Cv2.FindHomography(
            InputArray.Create(good.Select(m => ToDouble(a.Points[m.QueryIdx]))),
            InputArray.Create(good.Select(m => ToDouble(b.Points[m.TrainIdx]))),
            HomographyMethods.Ransac,
            RansacThresholdPixels,
            inliers);
        return (double)Cv2.CountNonZero(inliers) / Math.Min(a.Points.Length, b.Points.Length);
    }

    private static Point2d ToDouble(Point2f point) => new(point.X, point.Y);

    /// <summary>Each query's best match, kept only when clearly better than its second best.</summary>
    /// <param name="knn">Each query's two nearest matches.</param>
    internal static List<DMatch> GoodMatches(DMatch[][] knn)
        => [.. knn.Where(m => m.Length == 2 && m[0].Distance < Ratio * m[1].Distance).Select(m => m[0])];

    private static partial class Log
    {
        [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Could not read thumbnail {Thumbnail} for burst detection; it matches nothing.")]
        public static partial void Unreadable(ILogger logger, string thumbnail, Exception exception);
    }
}
