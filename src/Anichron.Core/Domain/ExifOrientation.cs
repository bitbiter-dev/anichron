namespace Anichron.Core.Domain;

public static class ExifOrientation
{
    public const int Upright = 1;
    public const int Max = 8;

    /// <summary>
    /// Whether the orientation includes a quarter turn (EXIF 5-8), so the upright image's width is the stored height.
    /// </summary>
    /// <param name="orientation">The EXIF orientation value, 1-8.</param>
    public static bool SwapsAxes(int orientation) => orientation is >= 5 and <= Max;
}
