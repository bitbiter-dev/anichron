namespace Anichron.API.Security;

public interface IGuidFactory
{
    Guid NewGuid();
}

public sealed class TimeOrderedGuidFactory : IGuidFactory
{
    public Guid NewGuid() => Guid.CreateVersion7();
}
