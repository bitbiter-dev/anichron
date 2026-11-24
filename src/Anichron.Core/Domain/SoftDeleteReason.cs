namespace Anichron.Core.Domain;

public enum SoftDeleteReason
{
    /// <summary>A complete crawl did not find the file, or the file moved over another row's path.</summary>
    Missing = 1,

    /// <summary>The file's path now holds content stored at another path.</summary>
    Overwritten = 2,

    /// <summary>The Live Photo's photo let go of this video for another one.</summary>
    Superseded = 3,
}
