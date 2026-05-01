namespace Anichron.Core.Domain;

public class Metadata
{
    // Primary Key and Foreign Key to MediaAsset
    public Guid AssetId { get; set; }
    public virtual MediaAsset Asset { get; set; } = null!;

    // Dimensions & Transform
    public int Width { get; set; }
    public int Height { get; set; }
    public int OrientationDegrees { get; set; }

    // Location
    public float? Latitude { get; set; }
    public float? Longitude { get; set; }

    // Hardware Specs
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }

    // Video/File Specifics
    public float? DurationSeconds { get; set; }
}
