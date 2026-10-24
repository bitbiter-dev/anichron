namespace Anichron.Worker.Bursts;

/// <summary>Judges whether two stored thumbnails show the same scene.</summary>
internal interface IThumbnailMatcher
{
    /// <summary>
    /// The share of keypoints that fit one consistent transform between the two thumbnails, from 0
    /// (nothing in common) to 1; 0 when either thumbnail cannot be read.
    /// </summary>
    /// <param name="thumbnailA">A thumbnail's <c>ProxyPath</c>, relative to the proxy root.</param>
    /// <param name="thumbnailB">The other thumbnail's <c>ProxyPath</c>.</param>
    /// <param name="ct">Cancels reading the thumbnails.</param>
    Task<double> ScoreAsync(string thumbnailA, string thumbnailB, CancellationToken ct);

    /// <summary>Drops every thumbnail described so far, so a long run does not hold them all.</summary>
    void Forget();
}
