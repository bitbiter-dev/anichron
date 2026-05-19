using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Tests.Unit.Ingestion.Pipeline;

public sealed class PipelineConfigurationExceptionTests
{
    [Fact]
    public void Parameterless_HasDefaultMessage()
    {
        var ex = new PipelineConfigurationException();
        ex.Message.Should().NotBeNullOrEmpty();
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void MessageAndInnerException_BothPreserved()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new PipelineConfigurationException("oops", inner);
        ex.Message.Should().Be("oops");
        ex.InnerException.Should().BeSameAs(inner);
    }
}
