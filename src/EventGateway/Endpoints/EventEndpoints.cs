using System.Text.Json;
using EventGateway.Models;
using EventGateway.Services;

namespace EventGateway.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        // Submit a new event
        app.MapPost("/events", async (
            SubmitEventRequest req,
            EventRepository repo,
            AccountServiceClient accountClient,
            ILogger<Program> logger) =>
        {
            // ── Validation ───────────────────────────────────────────────────
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(req.EventId))    errors.Add("eventId is required");
            if (string.IsNullOrWhiteSpace(req.AccountId))  errors.Add("accountId is required");
            if (string.IsNullOrWhiteSpace(req.Type))       errors.Add("type is required");
            if (req.Type != "CREDIT" && req.Type != "DEBIT") errors.Add("type must be CREDIT or DEBIT");
            if (req.Amount <= 0)                           errors.Add("amount must be greater than 0");
            if (string.IsNullOrWhiteSpace(req.Currency))   errors.Add("currency is required");

            if (errors.Any())
                return Results.BadRequest(new { errors });

            // ── Idempotency check ────────────────────────────────────────────
            var existing = await repo.GetEventAsync(req.EventId!);
            if (existing != null)
            {
                logger.LogInformation("Duplicate eventId {EventId} — returning original", req.EventId);
                return Results.Ok(existing);   // 200 with original, not 201
            }

            // ── Save to Gateway DB ───────────────────────────────────────────
            var record = new EventRecord
            {
                EventId        = req.EventId!,
                AccountId      = req.AccountId!,
                Type           = req.Type!,
                Amount         = req.Amount,
                Currency       = req.Currency!,
                EventTimestamp = req.EventTimestamp,
                MetadataJson   = req.Metadata.HasValue
                    ? JsonSerializer.Serialize(req.Metadata.Value)
                    : null
            };

            await repo.SaveEventAsync(record);

            // ── Call Account Service ─────────────────────────────────────────
            var (success, error) = await accountClient.ApplyTransactionAsync(record);
            if (!success)
            {
                logger.LogError("Account Service failed for event {EventId}: {Error}", req.EventId, error);
                return Results.Json(
                    new { error = "Event saved but account update failed", detail = error },
                    statusCode: 503);
            }

            logger.LogInformation("Event {EventId} processed successfully", req.EventId);
            return Results.Created($"/events/{record.EventId}", record);
        });

        // Get a single event by ID
        app.MapGet("/events/{id}", async (
            string id,
            EventRepository repo) =>
        {
            var evt = await repo.GetEventAsync(id);
            return evt is null ? Results.NotFound() : Results.Ok(evt);
        });

        // List events for an account
        app.MapGet("/events", async (
            string account,
            EventRepository repo) =>
        {
            var events = await repo.GetEventsByAccountAsync(account);
            return Results.Ok(events);
        });

        // Health check
        app.MapGet("/health", async (
            EventRepository repo,
            AccountServiceClient accountClient,
            ILogger<Program> logger) =>
        {
            try
            {
                await repo.GetEventsByAccountAsync("__health__");
                return Results.Ok(new { status = "healthy", database = "ok", service = "EventGateway" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health check failed");
                return Results.Json(
                    new { status = "unhealthy", database = "error", error = ex.Message },
                    statusCode: 503);
            }
        });
    }
}