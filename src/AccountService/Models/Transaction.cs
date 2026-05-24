namespace AccountService.Models;

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventId { get; set; } = null!;       // idempotency key
    public string AccountId { get; set; } = null!;
    public string Type { get; set; } = null!;           // CREDIT | DEBIT
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public DateTime EventTimestamp { get; set; }        // original event time
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}