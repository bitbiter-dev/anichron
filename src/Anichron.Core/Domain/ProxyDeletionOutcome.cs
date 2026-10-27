namespace Anichron.Core.Domain;

public enum ProxyDeletionOutcome
{
    Deleted = 1,
    SkippedReferenced = 2,
    SkippedChanged = 3,
    Missing = 4,
    Failed = 5,
}
