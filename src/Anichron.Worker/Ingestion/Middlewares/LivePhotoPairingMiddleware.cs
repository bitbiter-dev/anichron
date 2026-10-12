using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class LivePhotoPairingMiddleware(
    IMediaAssetRepository repository,
    IUnitOfWork unitOfWork,
    PairedMovResolver pairedMovResolver,
    ILogger<LivePhotoPairingMiddleware> logger) : IIngestionMiddleware
{
    public int Order => IngestionOrder.LivePhotoPairing;
    public bool CanInvoke(IngestionContext context) => context.Item.SecondaryFile is not null;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        // Ordering guarantees ContentHashingMiddleware ran first and set both hashes; suppression is safe.
        var stored = await repository.FindByHashAsync(context.ContentHash!, context.Config.Id, ct);
        if (stored is null || stored.PairedAssetId is not null)
        {
            await next(context, ct);
            return;
        }

        stored.MediaType = context.Item.PrimaryMediaType;
        stored.PairedAssetId = await pairedMovResolver.ResolveAsync(
            context.Item.SecondaryFile!, context.SecondaryHash!, context.Config.Id, stored.DateCaptured, ct);
        await unitOfWork.SaveChangesAsync(ct);
        Log.Paired(logger, context.Item.RelativePath, stored.Id);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Paired already-ingested {RelativePath} (asset {AssetId}) with its Live Photo video")]
        public static partial void Paired(ILogger logger, string relativePath, Guid assetId);
    }
}
