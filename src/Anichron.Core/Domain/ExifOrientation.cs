namespace Anichron.Core.Domain;

public static class ExifOrientation
{
    public const int Upright = 1;
    public const int FlipLeftRight = 2;
    public const int RotateHalfTurn = 3;
    public const int FlipTopBottom = 4;
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

    /// <summary>The orientation that shows stored pixels as the given orientations do, applied first to last.</summary>
    /// <param name="steps">EXIF orientation values, 1-8, in the order they are applied.</param>
    public static int Compose(IEnumerable<int> steps) => steps.Aggregate(Upright, Then);

    // Index = 4 × (flipped left-right first) + clockwise quarter turns after it.
    private static ReadOnlySpan<int> ByFlipAndTurns => [Upright, RotateClockwise, RotateHalfTurn, RotateAnticlockwise, FlipLeftRight, 7, FlipTopBottom, 5];

    private static int Then(int first, int second)
    {
        var a = ByFlipAndTurns.IndexOf(first);
        var b = ByFlipAndTurns.IndexOf(second);
        var turns = b < 4 ? b + a : b - a;
        return ByFlipAndTurns[((a ^ b) & 4) | (turns & 3)];
    }
}
