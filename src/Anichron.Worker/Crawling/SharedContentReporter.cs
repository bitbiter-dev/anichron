using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;

namespace Anichron.Worker.Crawling;

internal interface ISharedContentReporter
{
    Task ReportAsync(IReadOnlyCollection<UserStorageConfig> configs, CancellationToken ct);
}

internal sealed partial class SharedContentReporter(
    IMediaAssetRepository repository,
    DuplicateCopyTally tally,
    ILogger<SharedContentReporter> logger) : ISharedContentReporter
{
    public async Task ReportAsync(IReadOnlyCollection<UserStorageConfig> configs, CancellationToken ct)
    {
        var copiesByUser = configs
            .GroupBy(config => config.UserId)
            .Select(user => (UserId: user.Key, Copies: user.Sum(config => tally.Take(config.Id))))
            .ToList();

        var locations = await repository.FindSharedContentAsync([.. configs.Select(config => config.Id)], ct);
        var pairsByUser = new Dictionary<Guid, int>();
        foreach (var content in locations.GroupBy(location => (location.UserId, location.ContentHash)))
        {
            var copies = content.OrderBy(location => location.StorageConfigId).ToList();
            var pairs = copies.SelectMany((first, index) => copies.Skip(index + 1).Select(second => (first, second)));
            foreach (var (first, second) in pairs)
            {
                Log.SharedContent(
                    logger, first.ContentHash, first.UserId,
                    first.FilePath, first.StorageConfigId, second.FilePath, second.StorageConfigId);
                pairsByUser[first.UserId] = pairsByUser.GetValueOrDefault(first.UserId) + 1;
            }
        }

        foreach (var (userId, copies) in copiesByUser)
        {
            var pairs = pairsByUser.GetValueOrDefault(userId);
            if (copies + pairs > 0)
                Log.Summary(logger, userId, copies, pairs);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Duplicate content {ContentHash} for user {UserId}: {FilePath} in storage config {StorageConfigId} "
                + "and {OtherFilePath} in storage config {OtherStorageConfigId}.")]
        public static partial void SharedContent(
            ILogger logger, string contentHash, Guid userId,
            string filePath, Guid storageConfigId, string otherFilePath, Guid otherStorageConfigId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Duplicate content for user {UserId}: copies within one storage config: {WithinConfigCopies}, "
                + "pairs across storage configs: {AcrossConfigPairs}.")]
        public static partial void Summary(ILogger logger, Guid userId, int withinConfigCopies, int acrossConfigPairs);
    }
}
