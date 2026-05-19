using Anichron.Worker.Ingestion.Pipeline;
using System.IO.Abstractions;
using System.IO.Hashing;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed class ContentHashingMiddleware(IFileSystem fileSystem) : IIngestionMiddleware
{
    public int Order => IngestionOrder.ContentHashing;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        context.ContentHash = await HashFileAsync(context.Item.AbsolutePath, ct);
        if (context.Item.SecondaryFile is { } secondary)
            context.SecondaryHash = await HashFileAsync(secondary.AbsolutePath, ct);
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
