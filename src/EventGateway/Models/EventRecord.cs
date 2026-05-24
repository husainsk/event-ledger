namespace EventGateway.Models;

public class EventRecord
{
    public string EventId { get; set; } = null!;        // PK — idempotency key
    public string AccountId { get; set; } = null!;
    public string Type { get; set; } = null!;           // CREDIT | DEBIT
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public DateTime EventTimestamp { get; set; }        // original event time
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string? MetadataJson { get; set; }           // serialized metadata blob
}