namespace Anichron.Core.Data;

public static class MediaAssetIndexNames
{
    public const string ActiveContentHashUnique = "ix_mediaassets_storageconfigid_contenthash_active";
    public const string SoftDeletedContentHash = "ix_mediaassets_storageconfigid_contenthash_softdeleted";
    public const string FilePathUnique = "IX_MediaAssets_StorageConfigId_FilePath";
    public const string PairedAssetIdUnique = "IX_MediaAssets_PairedAssetId";
}
