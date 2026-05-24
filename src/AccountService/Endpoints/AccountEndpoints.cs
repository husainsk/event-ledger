using AccountService.Models;
using AccountService.Services;

namespace AccountService.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        // Apply a transaction (called by Gateway only)
        app.MapPost("/accounts/{accountId}/transactions", async (
            string accountId,
            ApplyTransactionRequest req,
            AccountRepository repo,
            ILogger<Program> logger) =>
        {
            if (await repo.EventExistsAsync(req.EventId))
            {
                logger.LogInformation("Duplicate event {EventId} — skipping", req.EventId);
                return Results.Ok(new { message = "Duplicate — already applied", eventId = req.EventId });
            }

            var tx = await repo.ApplyTransactionAsync(req);
            var balance = await repo.GetBalanceAsync(accountId);

            logger.LogInformation("Applied {Type} of {Amount} to {AccountId}. Balance: {Balance}",
                req.Type, req.Amount, accountId, balance);

            return Results.Created($"/accounts/{accountId}", new
            {
                transactionId = tx.Id,
                accountId,
                balance,
                currency = req.Currency
            });
        });

        // Get current balance
        app.MapGet("/accounts/{accountId}/balance", async (
            string accountId,
            AccountRepository repo) =>
        {
            var balance = await repo.GetBalanceAsync(accountId);
            return Results.Ok(new { accountId, balance });
        });

        // Get account details + transaction history
        app.MapGet("/accounts/{accountId}", async (
            string accountId,
            AccountRepository repo) =>
        {
            var transactions = await repo.GetTransactionsAsync(accountId);
            var balance = transactions.Any()
                ? transactions.Where(t => t.Type == "CREDIT").Sum(t => t.Amount)
                - transactions.Where(t => t.Type == "DEBIT").Sum(t => t.Amount)
                : 0m;

            return Results.Ok(new { accountId, balance, transactions });
        });

        // Health check
        app.MapGet("/health", async (AccountRepository repo, ILogger<Program> logger) =>
        {
            try
            {
                await repo.GetBalanceAsync("__health__");
                return Results.Ok(new { status = "healthy", database = "ok", service = "AccountService" });
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