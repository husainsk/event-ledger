using EventGateway.Data;
using EventGateway.Endpoints;
using EventGateway.Services;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.Extensions.Http;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog structured JSON logging ─────────────────────────────────────────
builder.Host.UseSerilog((ctx, config) =>
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "EventGateway")
        .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

// ── SQLite database ──────────────────────────────────────────────────────────
builder.Services.AddDbContext<GatewayDbContext>(options =>
    options.UseSqlite("Data Source=gateway.db"));

// ── Business logic ───────────────────────────────────────────────────────────
builder.Services.AddScoped<EventRepository>();

// ── Polly policies ───────────────────────────────────────────────────────────
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        onRetry: (outcome, duration, attempt, ctx) =>
            Log.Warning("Retry {Attempt} after {Delay}s. Reason: {Reason}",
                attempt, duration.TotalSeconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()));

var circuitBreakerPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30),
        onBreak: (outcome, duration) =>
            Log.Warning("Circuit OPEN for {Duration}s. Reason: {Reason}",
                duration.TotalSeconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()),
        onReset: () => Log.Information("Circuit CLOSED — Account Service recovered"),
        onHalfOpen: () => Log.Information("Circuit HALF-OPEN — testing Account Service"));

// ── HttpClient for Account Service with Polly ────────────────────────────────
builder.Services.AddHttpClient<AccountServiceClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["AccountService:BaseUrl"] ?? "http://localhost:5087");
    client.Timeout = TimeSpan.FromSeconds(10);
})
.AddPolicyHandler(retryPolicy)
.AddPolicyHandler(circuitBreakerPolicy);

// ── OpenTelemetry tracing ────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("EventGateway"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()     // auto-propagates traceparent to Account Service
        .AddConsoleExporter());

var app = builder.Build();

// ── Create DB schema on startup ──────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    db.Database.EnsureCreated();
}

app.MapEventEndpoints();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }