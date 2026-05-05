using Anichron.Core.Common;

namespace Anichron.Core.Domain;

public class ProxyFile
{
    public Guid Id { get; set; }

    public Guid AssetId { get; set; }

    public string ProxyPath { get; set; } = string.Empty;

    // Categorization: e.g., "Thumbnail", "StoryVideo", "BlurHash"
    public ProxyType ProxyType { get; set; }

    public long SizeBytes { get; set; }
    public Instant CreatedAt { get; set; }

    // Navigation Properties
    public virtual MediaAsset Asset { get; set; } = null!;
}
