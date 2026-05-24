using AccountService.Data;
using AccountService.Endpoints;
using AccountService.Services;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog structured JSON logging ─────────────────────────────────────────
builder.Host.UseSerilog((ctx, config) =>
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "AccountService")
        .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

// ── SQLite database ──────────────────────────────────────────────────────────
builder.Services.AddDbContext<AccountDbContext>(options =>
    options.UseSqlite("Data Source=account-service.db"));

// ── Business logic ───────────────────────────────────────────────────────────
builder.Services.AddScoped<AccountRepository>();

// ── OpenTelemetry tracing ────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("AccountService"))
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

var app = builder.Build();

// ── Create DB schema on startup ──────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
    db.Database.EnsureCreated();
}

app.MapAccountEndpoints();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }