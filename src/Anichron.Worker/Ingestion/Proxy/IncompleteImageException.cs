namespace Anichron.Worker.Ingestion.Proxy;

public sealed class IncompleteImageException : Exception
{
    public IncompleteImageException() { }
    public IncompleteImageException(string message) : base(message) { }
    public IncompleteImageException(string message, Exception innerException) : base(message, innerException) { }
}
