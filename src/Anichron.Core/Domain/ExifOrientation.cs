namespace Anichron.Core.Domain;

public static class ExifOrientation
{
    public const int Upright = 1;
    public const int RotateHalfTurn = 3;
    public const int RotateClockwise = 6;
    public const int RotateAnticlockwise = 8;
    public const int Max = 8;

    /// <summary>
    /// Whether the orientation includes a quarter turn (EXIF 5-8), so the upright image's width is the stored height.
    /// </summary>
    /// <param name="orientation">The EXIF orientation value, 1-8.</param>
    public static bool SwapsAxes(int orientation) => orientation is >= 5 and <= Max;

    /// <summary>
    /// The orientation that shows stored pixels as a display rotation does (a HEIF <c>irot</c>, a QuickTime
    /// track's rotation); a rotation that is not a multiple of 90 degrees is upright.
    /// </summary>
    /// <param name="degrees">The display rotation, anticlockwise.</param>
    public static int OfDisplayRotation(double degrees)
        => ((((int)Math.Round(degrees) % 360) + 360) % 360) switch
        {
            90 => RotateAnticlockwise,
            180 => RotateHalfTurn,
            270 => RotateClockwise,
            _ => Upright,
        };
}
