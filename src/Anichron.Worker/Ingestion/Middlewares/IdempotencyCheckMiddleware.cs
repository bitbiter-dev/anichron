using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class IdempotencyCheckMiddleware(
    IMediaAssetRepository repository,
    IUnitOfWork unitOfWork,
    IFileSystem fileSystem,
    IClock clock,
    ILogger<IdempotencyCheckMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.IdempotencyCheck;
    public bool CanInvoke(IngestionContext context) => true;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        // Ordering guarantees ContentHashingMiddleware ran first; suppression is safe.
        var existing = await repository.FindByHashAsync(context.ContentHash!, context.Config.Id, ct);
        if (existing is null)
        {
            await next(context, ct);
            return;
        }

        var changed = Track(existing, context.Item.RelativePath, context.Config.RootPath);
        if (context.Item.SecondaryFile is { } secondary
            // Ordering guarantees ContentHashingMiddleware ran first and set SecondaryHash; suppression is safe.
            && await repository.FindByHashAsync(context.SecondaryHash!, context.Config.Id, ct) is { } storedSecondary)
        {
            changed |= Track(storedSecondary, secondary.RelativePath, context.Config.RootPath);
        }

        if (!changed)
            return;

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsFilePathConflict(ex))
        {
            Log.PathTaken(logger, context.Item.RelativePath);
        }
    }

    private static bool IsFilePathConflict(DbUpdateException ex)
        => ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: MediaAssetIndexNames.FilePathUnique,
        };

    private bool Track(MediaAsset stored, string relativePath, string rootPath)
    {
        if (stored.FilePath == relativePath)
        {
            stored.LastSeenOnNas = clock.GetCurrentInstant();
            Log.AlreadyIngested(logger, relativePath);
            return true;
        }

        if (fileSystem.File.Exists(fileSystem.Path.Combine(rootPath, stored.FilePath)))
        {
            Log.DuplicateCopy(logger, relativePath, stored.FilePath);
            return false;
        }

        Log.Moved(logger, stored.Id, stored.FilePath, relativePath);
        stored.FilePath = relativePath;
        stored.FileName = Path.GetFileName(relativePath);
        stored.LastSeenOnNas = clock.GetCurrentInstant();
        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Already ingested {RelativePath}, refreshed last seen.")]
        public static partial void AlreadyIngested(ILogger logger, string relativePath);

        [LoggerMessage(Level = LogLevel.Debug, Message = "{RelativePath} is a copy of already-ingested {StoredPath}, skipping.")]
        public static partial void DuplicateCopy(ILogger logger, string relativePath, string storedPath);

        [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} moved from {OldPath} to {NewPath}")]
        public static partial void Moved(ILogger logger, Guid assetId, string oldPath, string newPath);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot record {RelativePath}: another asset row already holds that path, skipping.")]
        public static partial void PathTaken(ILogger logger, string relativePath);
    }
}
