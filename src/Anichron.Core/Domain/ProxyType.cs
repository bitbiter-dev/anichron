namespace Anichron.Core.Domain;

public enum ProxyType
{
    /// <summary> Tiny, highly compressed image for grid views </summary>
    Thumbnail = 1,

    /// <summary> H.264 video for smooth mobile/web playback, sized by the Worker's VideoShortEdge </summary>
    WebVideo = 2,

    /// <summary> Optimized full-screen JPEG for Story view </summary>
    FullPreview = 3,
}
