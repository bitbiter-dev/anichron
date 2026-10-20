using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;

namespace Anichron.Worker.Crawling;

/// <summary>Duplicate content found by one config's crawl.</summary>
/// <param name="CopiesWithin">Second copies of stored content at another path in the crawled config.</param>
/// <param name="PairsAcross">Pairs of the same content in the crawled config and another of the user's configs.</param>
public sealed record DuplicateCounts(int CopiesWithin, int PairsAcross);

internal interface ISharedContentReporter
{
    Task<DuplicateCounts> ReportAsync(
        UserStorageConfig crawled, IReadOnlyCollection<UserStorageConfig> usersConfigs, CancellationToken ct);
}

internal sealed partial class SharedContentReporter(
    IMediaAssetRepository repository,
    DuplicateCopyTally tally,
    ILogger<SharedContentReporter> logger) : ISharedContentReporter
{
    public async Task<DuplicateCounts> ReportAsync(
        UserStorageConfig crawled, IReadOnlyCollection<UserStorageConfig> usersConfigs, CancellationToken ct)
    {
        var copies = tally.Take(crawled.Id);
        var configIds = usersConfigs.Select(config => config.Id).Append(crawled.Id).Distinct().ToList();
        var locations = await repository.FindSharedContentAsync(configIds, ct);

        var pairs = 0;
        foreach (var content in locations.GroupBy(location => location.ContentHash))
        {
            var places = content.OrderBy(location => location.StorageConfigId).ToList();
            var crawledPairs = places
                .SelectMany((first, index) => places.Skip(index + 1).Select(second => (first, second)))
                .Where(pair => pair.first.StorageConfigId == crawled.Id || pair.second.StorageConfigId == crawled.Id);
            foreach (var (first, second) in crawledPairs)
            {
                Log.SharedContent(
                    logger, first.ContentHash, first.UserId,
                    first.FilePath, first.StorageConfigId, second.FilePath, second.StorageConfigId);
                pairs++;
            }
        }

        if (copies + pairs > 0)
            Log.Summary(logger, crawled.Id, crawled.UserId, copies, pairs);
        return new DuplicateCounts(copies, pairs);
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
            Message = "Duplicate content in storage config {StorageConfigId} of user {UserId}: copies within it: "
                + "{WithinConfigCopies}, pairs with the user's other storage configs: {AcrossConfigPairs}.")]
        public static partial void Summary(
            ILogger logger, Guid storageConfigId, Guid userId, int withinConfigCopies, int acrossConfigPairs);
    }
}
