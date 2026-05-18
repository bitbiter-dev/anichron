namespace Anichron.Worker.Ingestion.Pipeline;

public sealed class PipelineConfigurationException : Exception
{
    public PipelineConfigurationException() { }

    public PipelineConfigurationException(string message) : base(message) { }

    public PipelineConfigurationException(string? message, Exception? innerException) : base(message, innerException) { }
}
