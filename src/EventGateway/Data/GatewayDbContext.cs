using EventGateway.Models;
using Microsoft.EntityFrameworkCore;

namespace EventGateway.Data;

public class GatewayDbContext : DbContext
{
    public GatewayDbContext(DbContextOptions<GatewayDbContext> options) : base(options) { }

    public DbSet<EventRecord> Events => Set<EventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventRecord>()
            .HasKey(e => e.EventId);                    // EventId is the primary key

        modelBuilder.Entity<EventRecord>()
            .HasIndex(e => e.AccountId);                // faster account queries
    }
}