namespace Anichron.Core.Domain;

public class WorkItem
{
    public Guid Id { get; set; }
    public WorkKind Kind { get; set; }
    public required string Payload { get; set; }
    public string? DedupKey { get; set; }
    public Guid? RequestedBy { get; set; }
    public Instant RequestedAt { get; set; }

    public WorkItemStatus Status { get; set; }
    public int Attempt { get; set; }
    public int Stalls { get; set; }
    public string? ClaimedBy { get; set; }
    public Instant? StartedAt { get; set; }
    public Instant? LeaseUntil { get; set; }
    public Instant? FinishedAt { get; set; }
    public Instant UpdatedAt { get; set; }

    public long ProgressDone { get; set; }
    public long? ProgressTotal { get; set; }
    public string? ProgressNote { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}
