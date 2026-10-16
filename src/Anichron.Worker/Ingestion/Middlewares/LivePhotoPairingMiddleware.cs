using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class LivePhotoPairingMiddleware(
    IMediaAssetRepository repository,
    IUnitOfWork unitOfWork,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger<LivePhotoPairingMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.LivePhotoPairing;
    public bool CanInvoke(IngestionContext context) => context.Item.SecondaryFile is not null;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        // Ordering guarantees ContentHashingMiddleware ran first and set both hashes; suppression is safe.
        var stored = await repository.FindByHashAsync(context.ContentHash!, context.Config.Id, ct);
        if (stored is null)
        {
            await next(context, ct);
            return;
        }

        if (stored.PairedAssetId is { } pairedAssetId)
        {
            await BackfillPairedMovProxiesAsync(context, pairedAssetId, ct);
            await next(context, ct);
            return;
        }

        stored.MediaType = context.Item.PrimaryMediaType;
        var mov = await pairedMovResolver.ResolveAsync(
            context.Item.SecondaryFile!, context.SecondaryHash!, context.Config.Id, stored.DateCaptured, ct);
        await pairedMovProxyWriter.WriteMissingAsync(context, mov, ct);
        stored.PairedAssetId = mov.Id;
        await SaveAsync(context, mov.Id, ct);
        Log.Paired(logger, context.Item.RelativePath, stored.Id);
    }

    private async Task BackfillPairedMovProxiesAsync(IngestionContext context, Guid pairedAssetId, CancellationToken ct)
    {
        var mov = await repository.FindByHashWithProxyFilesAsync(context.SecondaryHash!, context.Config.Id, ct);
        if (mov?.Id != pairedAssetId)
            return;

        if (await pairedMovProxyWriter.WriteMissingAsync(context, mov, ct))
            await SaveAsync(context, mov.Id, ct);
    }

    private async Task SaveAsync(IngestionContext context, Guid movId, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (LostIngestionRace.Matches(ex))
        {
            context.ReleaseProxyFiles(movId);
            throw;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Paired already-ingested {RelativePath} (asset {AssetId}) with its Live Photo video")]
        public static partial void Paired(ILogger logger, string relativePath, Guid assetId);
    }
}
