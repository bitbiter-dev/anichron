namespace Anichron.Core.Domain;

public static class ExifOrientation
{
    public const int Upright = 1;
    public const int FlipLeftRight = 2;
    public const int RotateHalfTurn = 3;
    public const int FlipTopBottom = 4;
    public const int Transpose = 5;
    public const int RotateClockwise = 6;
    public const int Transverse = 7;
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

    private const int flipped = 4;
    private const int turnsMask = 3;

    // Index: flipped (left-right, applied first) plus the clockwise quarter turns after it.
    private static ReadOnlySpan<int> ByFlipAndTurns
        => [Upright, RotateClockwise, RotateHalfTurn, RotateAnticlockwise, FlipLeftRight, Transverse, FlipTopBottom, Transpose];

    private static int Then(int first, int second)
    {
        var before = ByFlipAndTurns.IndexOf(first);
        var after = ByFlipAndTurns.IndexOf(second);
        var turns = (after & flipped) == 0 ? after + before : after - before;
        return ByFlipAndTurns[((before ^ after) & flipped) | (turns & turnsMask)];
    }
}
