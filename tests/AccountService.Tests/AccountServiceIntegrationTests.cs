using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AccountService.Tests;

public class AccountServiceIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AccountServiceIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private object MakeTransaction(string eventId, string accountId, string type, decimal amount) => new
    {
        eventId,
        accountId,
        type,
        amount,
        currency = "USD",
        eventTimestamp = DateTime.UtcNow
    };

    [Fact]
    public async Task ApplyTransaction_Credit_ReturnsCreatedWithBalance()
    {
        var accountId = $"acct-{Guid.NewGuid()}";
        var payload = MakeTransaction($"evt-{Guid.NewGuid()}", accountId, "CREDIT", 200m);

        var response = await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task ApplyTransaction_Idempotent_DoesNotDuplicateBalance()
    {
        var accountId = $"acct-{Guid.NewGuid()}";
        var eventId = $"evt-{Guid.NewGuid()}";
        var payload = MakeTransaction(eventId, accountId, "CREDIT", 100m);

        // Submit twice
        await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions", payload);
        await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions", payload);

        // Balance should still be 100, not 200
        var balanceResponse = await _client.GetAsync($"/accounts/{accountId}/balance");
        var balance = await balanceResponse.Content.ReadFromJsonAsync<BalanceResponse>();

        Assert.Equal(100m, balance!.Balance);
    }

    [Fact]
    public async Task Balance_CreditMinusDebit_IsCorrect()
    {
        var accountId = $"acct-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions",
            MakeTransaction($"evt-{Guid.NewGuid()}", accountId, "CREDIT", 300m));

        await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions",
            MakeTransaction($"evt-{Guid.NewGuid()}", accountId, "DEBIT", 80m));

        var balanceResponse = await _client.GetAsync($"/accounts/{accountId}/balance");
        var balance = await balanceResponse.Content.ReadFromJsonAsync<BalanceResponse>();

        Assert.Equal(220m, balance!.Balance);
    }

    [Fact]
    public async Task GetAccount_TransactionsOrderedByEventTimestamp()
    {
        var accountId = $"acct-{Guid.NewGuid()}";

        // Submit out of order — later timestamp first
        await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "CREDIT",
            amount = 50m,
            currency = "USD",
            eventTimestamp = new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc)  // earlier
        });

        await _client.PostAsJsonAsync($"/accounts/{accountId}/transactions", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "CREDIT",
            amount = 75m,
            currency = "USD",
            eventTimestamp = new DateTime(2026, 5, 14, 10, 0, 0, DateTimeKind.Utc)  // later arrival, earlier timestamp
        });

        var response = await _client.GetAsync($"/accounts/{accountId}");
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>();

        Assert.Equal(2, body!.Transactions.Count);
        Assert.True(body.Transactions[0].EventTimestamp < body.Transactions[1].EventTimestamp);
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

// Response DTOs for deserialization
public record BalanceResponse(string AccountId, decimal Balance);
public record TransactionItem(string EventId, DateTime EventTimestamp);
public record AccountResponse(string AccountId, decimal Balance, List<TransactionItem> Transactions);