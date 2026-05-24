using EventGateway.Data;
using EventGateway.Models;
using Microsoft.EntityFrameworkCore;

namespace EventGateway.Services;

public class EventRepository
{
    private readonly GatewayDbContext _db;

    public EventRepository(GatewayDbContext db)
    {
        _db = db;
    }

    public async Task<bool> EventExistsAsync(string eventId)
    {
        return await _db.Events.AnyAsync(e => e.EventId == eventId);
    }

    public async Task<EventRecord?> GetEventAsync(string eventId)
    {
        return await _db.Events.FindAsync(eventId);
    }

    public async Task<EventRecord> SaveEventAsync(EventRecord record)
    {
        _db.Events.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    public async Task<List<EventRecord>> GetEventsByAccountAsync(string accountId)
    {
        return await _db.Events
            .Where(e => e.AccountId == accountId)
            .OrderBy(e => e.EventTimestamp)             // chronological by event time
            .ToListAsync();
    }
}