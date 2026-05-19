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
        await using var stream = fileSystem.File.OpenRead(context.Item.AbsolutePath);
        var hasher = new XxHash64();
        await hasher.AppendAsync(stream, ct);
        context.ContentHash = Convert.ToHexStringLower(hasher.GetHashAndReset());
        await next(context, ct);
    }
}
