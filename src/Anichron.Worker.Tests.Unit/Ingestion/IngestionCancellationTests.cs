using Anichron.Worker.Ingestion;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class IngestionCancellationTests
{
    private static CancellationToken Token(bool cancelled) => new(cancelled);

    [Fact]
    public void IsRequested_CancellationWhileTheTokenIsCancelled_IsACancel()
        => IngestionCancellation.IsRequested(new OperationCanceledException(), Token(cancelled: true)).Should().BeTrue();

    [Fact]
    public void IsRequested_CancellationWithoutTheTokenCancelled_IsAFailure()
        => IngestionCancellation.IsRequested(new OperationCanceledException(), Token(cancelled: false)).Should().BeFalse();

    [Fact]
    public void IsRequested_OtherExceptionWhileTheTokenIsCancelled_IsAFailure()
        => IngestionCancellation.IsRequested(new IOException(), Token(cancelled: true)).Should().BeFalse();
}
