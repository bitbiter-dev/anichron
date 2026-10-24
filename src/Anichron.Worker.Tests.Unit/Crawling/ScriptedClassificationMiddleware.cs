using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Tests.Unit.Ingestion.Classification;

namespace Anichron.Worker.Tests.Unit.Crawling;

internal sealed record ScriptedItem(
    FileOutcome Primary,
    FileOutcome? Secondary = null,
    bool Fails = false,
    CancellationTokenSource? Cancels = null);

internal sealed class ScriptedClassificationMiddleware(params ScriptedItem[] itemsInOrder) : IIngestionMiddleware
{
    private int calls;

    public int Order => 10;
    public bool CanInvoke(IngestionContext context) => true;

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        _ = next;
        var item = itemsInOrder[calls++];
        if (item.Cancels is { } cancel)
        {
            await cancel.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }

        context.Classification = Classifications.Of(item.Primary, item.Secondary);
        if (item.Fails)
            throw new InvalidOperationException("boom");
    }
}
