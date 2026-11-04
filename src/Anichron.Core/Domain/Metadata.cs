namespace Anichron.Core.Domain;

public class Metadata
{
    // Primary Key and Foreign Key to MediaAsset
    public Guid AssetId { get; set; }
    public virtual MediaAsset Asset { get; set; } = null!;

    public MediaDescription Description { get; set; } = MediaDescription.Unknown;

    // Image Placeholder
    public string? Blurhash { get; set; }
}
