using AccountService.Models;
using Microsoft.EntityFrameworkCore;

namespace AccountService.Data;

public class AccountDbContext : DbContext
{
    public AccountDbContext(DbContextOptions<AccountDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>()
            .HasIndex(t => t.EventId)
            .IsUnique();                    // enforces idempotency at DB level

        modelBuilder.Entity<Transaction>()
            .HasIndex(t => t.AccountId);    // faster balance queries
    }
}