using AccountService.Data;
using AccountService.Models;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Services;

public class AccountRepository
{
    private readonly AccountDbContext _db;

    public AccountRepository(AccountDbContext db)
    {
        _db = db;
    }

    public async Task<bool> EventExistsAsync(string eventId)
    {
        return await _db.Transactions.AnyAsync(t => t.EventId == eventId);
    }

    public async Task<Transaction> ApplyTransactionAsync(ApplyTransactionRequest req)
    {
        var tx = new Transaction
        {
            EventId        = req.EventId,
            AccountId      = req.AccountId,
            Type           = req.Type,
            Amount         = req.Amount,
            Currency       = req.Currency,
            EventTimestamp = req.EventTimestamp,
        };

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }

    public async Task<decimal> GetBalanceAsync(string accountId)
    {
        var transactions = await _db.Transactions
            .Where(t => t.AccountId == accountId)
            .ToListAsync();

        decimal credits = transactions.Where(t => t.Type == "CREDIT").Sum(t => t.Amount);
        decimal debits  = transactions.Where(t => t.Type == "DEBIT").Sum(t => t.Amount);
        return credits - debits;
    }

    public async Task<List<Transaction>> GetTransactionsAsync(string accountId)
    {
        return await _db.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderBy(t => t.EventTimestamp)     // chronological by event time, not arrival
            .ToListAsync();
    }
}