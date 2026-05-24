namespace AccountService.Models;

public class ApplyTransactionRequest
{
    public string EventId { get; set; } = null!;
    public string AccountId { get; set; } = null!;
    public string Type { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public DateTime EventTimestamp { get; set; }
}