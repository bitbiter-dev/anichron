using Anichron.Worker.Ingestion;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class IngestionShutdownTests
{
    private static CancellationToken Token(bool cancelled) => new(cancelled);

    [Fact]
    public void IsInProgress_CancellationWhileTheTokenIsCancelled_IsAShutdown()
        => IngestionShutdown.IsInProgress(new OperationCanceledException(), Token(cancelled: true)).Should().BeTrue();

    [Fact]
    public void IsInProgress_CancellationWithoutTheTokenCancelled_IsAFailure()
        => IngestionShutdown.IsInProgress(new OperationCanceledException(), Token(cancelled: false)).Should().BeFalse();

    [Fact]
    public void IsInProgress_OtherExceptionWhileTheTokenIsCancelled_IsAFailure()
        => IngestionShutdown.IsInProgress(new IOException(), Token(cancelled: true)).Should().BeFalse();
}
