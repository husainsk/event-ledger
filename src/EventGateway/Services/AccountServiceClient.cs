using System.Text;
using System.Text.Json;
using EventGateway.Models;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;  

namespace EventGateway.Services;

public class AccountServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AccountServiceClient> _logger;

    public AccountServiceClient(HttpClient http, ILogger<AccountServiceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error)> ApplyTransactionAsync(EventRecord evt)
    {
        var payload = new
        {
            eventId        = evt.EventId,
            accountId      = evt.AccountId,
            type           = evt.Type,
            amount         = evt.Amount,
            currency       = evt.Currency,
            eventTimestamp = evt.EventTimestamp
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync(
                $"/accounts/{evt.AccountId}/transactions", content);

            if (response.IsSuccessStatusCode)
                return (true, null);

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Account Service returned {Status}: {Body}",
                response.StatusCode, body);
            return (false, $"Account Service error: {response.StatusCode}");
        }
        catch (BrokenCircuitException)
        {
            _logger.LogError("Circuit breaker is OPEN — Account Service unavailable");
            return (false, "Account Service is temporarily unavailable (circuit open)");
        }
        catch (TimeoutRejectedException)
        {
            _logger.LogError("Account Service call timed out");
            return (false, "Account Service timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Account Service");
            return (false, ex.Message);
        }
    }
}