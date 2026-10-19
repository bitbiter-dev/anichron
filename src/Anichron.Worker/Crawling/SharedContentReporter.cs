using Anichron.Core.Data.Repository;

namespace Anichron.Worker.Crawling;

internal interface ISharedContentReporter
{
    Task ReportAsync(IReadOnlyCollection<Guid> storageConfigIds, CancellationToken ct);
}

internal sealed partial class SharedContentReporter(
    IMediaAssetRepository repository,
    ILogger<SharedContentReporter> logger) : ISharedContentReporter
{
    public async Task ReportAsync(IReadOnlyCollection<Guid> storageConfigIds, CancellationToken ct)
    {
        var locations = await repository.FindSharedContentAsync(storageConfigIds, ct);
        foreach (var content in locations.GroupBy(location => (location.UserId, location.ContentHash)))
        {
            var copies = content.OrderBy(location => location.StorageConfigId).ToList();
            var pairs = copies.SelectMany((first, index) => copies.Skip(index + 1).Select(second => (first, second)));
            foreach (var (first, second) in pairs)
            {
                Log.SharedContent(
                    logger, first.ContentHash, first.UserId,
                    first.FilePath, first.StorageConfigId, second.FilePath, second.StorageConfigId);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Duplicate content {ContentHash} for user {UserId}: {FilePath} in storage config {StorageConfigId} "
                + "and {OtherFilePath} in storage config {OtherStorageConfigId}.")]
        public static partial void SharedContent(
            ILogger logger, string contentHash, Guid userId,
            string filePath, Guid storageConfigId, string otherFilePath, Guid otherStorageConfigId);
    }
}
