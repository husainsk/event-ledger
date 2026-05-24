using System.Text.Json;

namespace EventGateway.Models;

public class SubmitEventRequest
{
    public string? EventId { get; set; }
    public string? AccountId { get; set; }
    public string? Type { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime EventTimestamp { get; set; }
    public JsonElement? Metadata { get; set; }          // accepts any JSON object
}