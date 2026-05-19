namespace Anichron.Core;

public interface IGuidFactory
{
    Guid NewGuid();
}

public sealed class TimeOrderedGuidFactory : IGuidFactory
{
    public Guid NewGuid() => Guid.CreateVersion7();
}
