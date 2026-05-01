namespace Anichron.Core.Common;

public enum ProxyType
{
    /// <summary> Tiny, highly compressed image for grid views </summary>
    Thumbnail = 1,

    /// <summary> 720p H.264 video for smooth mobile/web playback </summary>
    WebVideo = 2,

    /// <summary> Optimized full-screen JPEG for Story view </summary>
    FullPreview = 3,

    /// <summary> Low-resolution string-based placeholder (optional) </summary>
    BlurHash = 4
}
