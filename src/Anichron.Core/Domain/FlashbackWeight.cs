namespace Anichron.Core.Domain;

/// <summary>
/// How strongly an asset counts when choosing flashback candidates. Computed from the caller's interaction, never stored.
/// </summary>
public static class FlashbackWeight
{
    public const double Default = 1.0;
    public const double Liked = 2.0;

    public static double For(bool liked) => liked ? Liked : Default;
}
