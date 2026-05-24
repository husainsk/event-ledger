# Event Ledger

A distributed financial transaction processing system composed of two microservices that handle out-of-order event delivery and duplicate submissions reliably.

---

## Architecture
Browser / Client ──→  Event Gateway API (port 5000)
│
│ REST + Polly (circuit breaker + retry)
▼
Account Service (port 5001)

### Event Gateway API (public-facing, port 5000)
- Accepts transaction events from clients
- Validates input and enforces idempotency
- Stores events in its own SQLite database
- Forwards transactions to Account Service via Polly-protected HTTP calls

### Account Service (internal, port 5001)
- Manages account balances and transaction history
- Enforces idempotency at the database level (unique index on `eventId`)
- Recomputes balance from all transactions on every request — arrival order is irrelevant

### Key Design Decisions

**Why recompute balance instead of a running total?**
Events arrive out of order. A running total would produce wrong results if a CREDIT from yesterday arrives after today's DEBIT. Recomputing from the full transaction set makes arrival order irrelevant.

**Why idempotency at both services?**
Defense in depth. The Gateway catches duplicates first (fast path). The Account Service also enforces a unique index on `eventId` so even if two Gateway instances raced, the database would reject the duplicate.

**Why circuit breaker + exponential backoff retry?**
Financial systems need fail-fast behavior. A hanging Gateway is worse than a fast 503. The circuit breaker prevents retry storms during outages and gives the Account Service time to recover. Exponential backoff (2s, 4s, 8s) avoids hammering a struggling service.

---

## Tech Stack

| Concern | Choice |
|---|---|
| Language | C# / .NET 10 |
| Framework | ASP.NET Core Minimal APIs |
| Database | SQLite via EF Core (one DB file per service) |
| Resiliency | Polly (circuit breaker + retry with exponential backoff) |
| Tracing | OpenTelemetry (auto-propagates `traceparent` headers) |
| Logging | Serilog (structured JSON logs with Service and TraceId) |
| Tests | xUnit + WireMock.Net |
| Containers | Docker Compose |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (optional)

---

## Running with Docker Compose (recommended)

```bash
docker compose up --build
```

Both services start automatically in the correct order:
- Event Gateway: http://localhost:5000
- Account Service: http://localhost:5001

---

## Running Locally (without Docker)

Open two terminals.

**Terminal 1 — Account Service:**
```bash
cd src/AccountService
dotnet run
```
Note the port (e.g. `http://localhost:5087`)

**Terminal 2 — Event Gateway:**
```bash
cd src/EventGateway
dotnet run
```
Note the port (e.g. `http://localhost:5246`)

To point the Gateway at the correct Account Service port, set this environment variable before running:
```bash
$env:AccountService__BaseUrl = "http://localhost:5087"
dotnet run
```

---

## Running the Tests

```bash
dotnet test
```

Expected output: **17 tests, 0 failed**

### What the tests cover

| Test | Validates |
|---|---|
| `PostEvent_ValidPayload_Returns201` | Happy path event submission |
| `PostEvent_DuplicateEventId_Returns200WithOriginal` | Gateway idempotency |
| `PostEvent_MissingEventId_Returns400` | Input validation |
| `PostEvent_ZeroAmount_Returns400` | Input validation |
| `PostEvent_InvalidType_Returns400` | Input validation |
| `GetEvent_ExistingId_Returns200` | Event retrieval |
| `GetEvent_NonExistentId_Returns404` | Not found handling |
| `GetEventsByAccount_ReturnsChronologicalOrder` | Out-of-order tolerance |
| `PostEvent_AccountServiceDown_Returns503` | Graceful degradation |
| `GetEvent_WorksEvenWhenAccountServiceDown` | Partial availability |
| `TraceId_PropagatedToAccountService` | Distributed trace propagation |
| `ApplyTransaction_Idempotent_DoesNotDuplicateBalance` | Account Service idempotency |
| `Balance_CreditMinusDebit_IsCorrect` | Balance computation |
| `GetAccount_TransactionsOrderedByEventTimestamp` | Out-of-order tolerance |
| `Health_ReturnsHealthy` (x2) | Health checks on both services |

---

## API Reference

### Event Gateway (port 5000)

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/events` | Submit a transaction event |
| `GET` | `/events/{id}` | Get a single event by ID |
| `GET` | `/events?account={accountId}` | List events for an account |
| `GET` | `/health` | Health check |

**Example POST /events:**
```json
{
  "eventId": "evt-001",
  "accountId": "acct-123",
  "type": "CREDIT",
  "amount": 150.00,
  "currency": "USD",
  "eventTimestamp": "2026-05-15T14:02:11Z",
  "metadata": {
    "source": "mainframe-batch",
    "batchId": "B-9042"
  }
}
```

### Account Service (port 5001)

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/accounts/{accountId}/transactions` | Apply a transaction |
| `GET` | `/accounts/{accountId}/balance` | Get current balance |
| `GET` | `/accounts/{accountId}` | Get account details + history |
| `GET` | `/health` | Health check |

---

## Observability

- **Structured logging** — Both services emit JSON logs with `Timestamp`, `Level`, `Service`, and `TraceId` on every request
- **Distributed tracing** — OpenTelemetry automatically propagates `traceparent` headers from Gateway to Account Service, linking spans across both services under one trace ID
- **Health endpoints** — Both services expose `/health` which checks actual database connectivity, not just process liveness

---

## Resiliency Pattern

The Gateway wraps all Account Service calls with a **Polly pipeline**:

1. **Retry with exponential backoff** — retries up to 3 times on transient failures (2s, 4s, 8s delays)
2. **Circuit breaker** — after 5 consecutive failures, the circuit opens for 30 seconds, returning fast 503s instead of waiting for timeouts

**Graceful degradation** — when the Account Service is unavailable:
- `POST /events` returns `503 Service Unavailable` (event is still saved in Gateway DB)
- `GET /events/{id}` and `GET /events?account=` continue to work normally
- Balance queries return a clear error indicating the Account Service is unreachable