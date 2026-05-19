using Anichron.Worker.Ingestion.Pipeline;
using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Hashing;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed class ContentHashingMiddleware(IFileSystem fileSystem) : IIngestionMiddleware
{
    public bool CanInvoke(IngestionContext context) => true;

    public IngestionStepError OnCannotInvoke(IngestionContext context) => throw new UnreachableException();

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        context.ContentHash = await HashFileAsync(context.Item.AbsolutePath, ct);

        if (context.Item is LivePhotoPairItem livePhoto)
            context.MovContentHash = await HashFileAsync(livePhoto.MovAbsolutePath, ct);

        await next(context, ct);
    }

    private async Task<string> HashFileAsync(string absolutePath, CancellationToken ct)
    {
        await using var stream = fileSystem.File.OpenRead(absolutePath);
        var hasher = new XxHash64();
        await hasher.AppendAsync(stream, ct);
        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }
}
