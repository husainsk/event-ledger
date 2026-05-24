using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace EventGateway.Tests;

public class EventGatewayIntegrationTests : IDisposable
{
    private readonly WireMockServer _accountServiceMock;
    private readonly HttpClient _client;

    public EventGatewayIntegrationTests()
    {
        // Start a mock Account Service on a random port
        _accountServiceMock = WireMockServer.Start();

        // Default: Account Service accepts all transactions
        _accountServiceMock
            .Given(Request.Create().WithPath("/accounts/*/transactions").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBodyAsJson(new { transactionId = Guid.NewGuid(), balance = 100m }));

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    // Point Gateway's HttpClient to our mock
                    services.AddHttpClient<EventGateway.Services.AccountServiceClient>(client =>
                    {
                        client.BaseAddress = new Uri(_accountServiceMock.Url!);
                        client.Timeout = TimeSpan.FromSeconds(5);
                    });
                });
            });

        _client = factory.CreateClient();
    }

    private object MakeEvent(string? eventId = null, string? accountId = null,
        string type = "CREDIT", decimal amount = 100m) => new
    {
        eventId = eventId ?? $"evt-{Guid.NewGuid()}",
        accountId = accountId ?? $"acct-{Guid.NewGuid()}",
        type,
        amount,
        currency = "USD",
        eventTimestamp = DateTime.UtcNow
    };

    [Fact]
    public async Task PostEvent_ValidPayload_Returns201()
    {
        var response = await _client.PostAsJsonAsync("/events", MakeEvent());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostEvent_MissingEventId_Returns400()
    {
        var payload = new { accountId = "acct-1", type = "CREDIT", amount = 100m, currency = "USD", eventTimestamp = DateTime.UtcNow };
        var response = await _client.PostAsJsonAsync("/events", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostEvent_ZeroAmount_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/events", MakeEvent(amount: 0m));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostEvent_InvalidType_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/events", MakeEvent(type: "TRANSFER"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostEvent_DuplicateEventId_Returns200WithOriginal()
    {
        var eventId = $"evt-{Guid.NewGuid()}";

        var first = await _client.PostAsJsonAsync("/events", MakeEvent(eventId: eventId));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/events", MakeEvent(eventId: eventId));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);  // not 201
    }

    [Fact]
    public async Task GetEvent_ExistingId_Returns200()
    {
        var eventId = $"evt-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/events", MakeEvent(eventId: eventId));

        var response = await _client.GetAsync($"/events/{eventId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetEvent_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync("/events/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEventsByAccount_ReturnsChronologicalOrder()
    {
        var accountId = $"acct-{Guid.NewGuid()}";

        // Submit out of order
        await _client.PostAsJsonAsync("/events", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "CREDIT",
            amount = 50m,
            currency = "USD",
            eventTimestamp = new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc)
        });

        await _client.PostAsJsonAsync("/events", new
        {
            eventId = $"evt-{Guid.NewGuid()}",
            accountId,
            type = "CREDIT",
            amount = 75m,
            currency = "USD",
            eventTimestamp = new DateTime(2026, 5, 14, 10, 0, 0, DateTimeKind.Utc)
        });

        var response = await _client.GetAsync($"/events?account={accountId}");
        var events = await response.Content.ReadFromJsonAsync<List<EventItem>>();

        Assert.Equal(2, events!.Count);
        Assert.True(events[0].EventTimestamp < events[1].EventTimestamp);
    }

    [Fact]
    public async Task PostEvent_AccountServiceDown_Returns503()
    {
        // Override mock to return 500
        _accountServiceMock
            .Given(Request.Create().WithPath("/accounts/*/transactions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var response = await _client.PostAsJsonAsync("/events", MakeEvent());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GetEvent_WorksEvenWhenAccountServiceDown_Returns200()
    {
        // First save an event while Account Service is up
        var eventId = $"evt-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/events", MakeEvent(eventId: eventId));

        // Now bring Account Service down
        _accountServiceMock
            .Given(Request.Create().WithPath("/accounts/*/transactions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        // GET should still work — reads from Gateway DB only
        var response = await _client.GetAsync($"/events/{eventId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TraceId_PropagatedToAccountService()
    {
         // Submit an event
         await _client.PostAsJsonAsync("/events", MakeEvent());

        // Verify the mock received a request with traceparent header
        var requests = _accountServiceMock.LogEntries.ToList();
        Assert.NotEmpty(requests);

        var hasTraceParent = requests.Any(r =>
            r.RequestMessage.Headers != null &&
            r.RequestMessage.Headers.ContainsKey("traceparent"));

        Assert.True(hasTraceParent, "traceparent header was not propagated to Account Service");
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _accountServiceMock.Stop();
        _client.Dispose();
    }
}

public record EventItem(string EventId, DateTime EventTimestamp);