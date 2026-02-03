# CloudScale Event Platform — Code ↔ Decision Mapping

This document links architectural decisions (D00X) and failure scenarios (F00X) to specific code locations. Use this to:

- Understand **where** decisions are implemented
- Identify **where** failures will manifest
- Navigate quickly during debugging or review

---

## Decision → Code Index

| Decision | Summary | Code Location |
|----------|---------|---------------|
| D001 | Service Bus over Kafka | [ServiceBusProducerService.cs](#d001-service-bus-implementation) |
| D002 | CQRS / Eventual Consistency | [EventProcessorWorker.cs](#d002-cqrs-implementation), [ReadEndpoints.cs](#d002-read-path) |
| D003 | Time-Based Partition Key | [CosmosDbService.cs](#d003-partition-key-implementation) |
| D004 | Consumer Idempotency | [CosmosDbService.cs](#d004-idempotency-implementation) |
| D005 | Default Circuit Breakers | [ServiceBusProducerService.cs](#d005-circuit-breaker-service-bus), [CosmosDbService.cs](#d005-circuit-breaker-cosmos) |
| D006 | No Multi-Region | [docker-compose.yml](#d006-single-region-config) |
| D007 | Observability Priority | [EventMetrics.cs](#d007-observability-implementation) |

---

## Failure → Code Index

| Failure | Summary | Will Break At |
|---------|---------|---------------|
| F001 | RU Exhaustion + DLQ | [CosmosDbService.AddEventAsync](#f001-ru-exhaustion-point) |
| F002 | Service Bus Ceiling | [ServiceBusProducerService.PublishAsync](#f002-service-bus-bottleneck) |
| F003 | CB False Positive | [ServiceBusProducerService circuit breaker](#f003-circuit-breaker-trigger) |
| F004 | Idempotency Collision | [CosmosEventDocument constructor](#f004-idempotency-collision-point) |
| F005 | Observability Overhead | [EventProcessorWorker.MessageHandler](#f005-tracing-overhead) |

---

## D001: Service Bus Implementation

**Decision:** Azure Service Bus over Apache Kafka

**File:** `src/CloudScale.IngestionApi/Services/ServiceBusProducerService.cs`

```csharp
// Lines 26-34: Service Bus client initialization
public ServiceBusProducerService(ServiceBusClient client, IConfiguration config, ...)
{
    _client = client;
    var queueName = config["ServiceBus:QueueName"];
    _sender = _client.CreateSender(queueName);
    // ...
}
```

**What this means:**
- Vendor lock-in happens here — `ServiceBusClient` is Azure-specific
- Switching to Kafka requires rewriting this entire service
- Queue name comes from config, but queue semantics are Service Bus-native

**Config location:** `ServiceBusConfig.json`, `docker-compose.yml` (emulator)

---

## D002: CQRS Implementation

**Decision:** Separate read and write paths

### Write Path

**File:** `src/CloudScale.EventProcessor/Workers/EventProcessorWorker.cs`

```csharp
// Line 117-214: MessageHandler processes events and writes to Cosmos
private async Task MessageHandler(ProcessMessageEventArgs args)
{
    // ... validation and fraud detection ...
    await _cosmosService.AddEventAsync(@event, args.CancellationToken);
    // ...
}
```

### Read Path

**File:** `src/CloudScale.IngestionApi/Endpoints/ReadEndpoints.cs`

Reads go through separate endpoints that query read models, not the write store directly.

**What this means:**
- Write and read are decoupled — write throughput is independent
- Read lag is inherent — consumers must tolerate stale data
- Two places to debug if data seems inconsistent

---

## D003: Partition Key Implementation

**Decision:** Time-based partition key (TenantId:yyyy-MM)

**File:** `src/CloudScale.EventProcessor/Services/CosmosDbService.cs`

```csharp
// Lines 145-153: CosmosEventDocument constructor
public CosmosEventDocument(EventBase @event)
{
    id = @event.DeduplicationId ?? @event.EventId; 
    EventData = @event;
    // Synthetic PK: TenantId:yyyy-MM (Principal-grade Hybrid Partitioning)
    PartitionKey = $"{@event.TenantId}:{@event.CreatedAt:yyyy-MM}";
}
```

**What this means:**
- Partition key = `{TenantId}:{yyyy-MM}` (e.g., `tenant1:2026-02`)
- **Hot partition risk:** All events in a month go to same partition
- **Query optimization:** Recent-data queries are efficient within partition
- **RU spike risk:** High-traffic hours within a month concentrate load

**Where F001 breaks:** When this partition gets throttled, `AddEventAsync` fails.

---

## D004: Idempotency Implementation

**Decision:** Consumer-level idempotency (not broker-guaranteed)

**File:** `src/CloudScale.EventProcessor/Services/CosmosDbService.cs`

```csharp
// Lines 63-91: AddEventAsync with conflict handling
public async Task AddEventAsync(EventBase @event, CancellationToken ct)
{
    var document = new CosmosEventDocument(@event);
    
    await _resiliencePipeline.ExecuteAsync(async token =>
    {
        try
        {
            await _container.CreateItemAsync<dynamic>(document, ...);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Principal Safeguard: 2-step Idempotency Validation
            var existing = await _container.ReadItemAsync<CosmosEventDocument>(...);
            
            if (existing.Resource.EventData.PayloadHash != @event.PayloadHash)
            {
                _logger.LogCritical("IDEMPOTENCY COLLISION: Event {EventId} received with DIFFERENT payload!", @event.EventId);
            }
            else
            {
                _logger.LogWarning("Duplicate event {EventId} with identical payload (ignored)", @event.EventId);
            }
        }
    }, ct);
}
```

**What this means:**
- Cosmos 409 Conflict = duplicate detected
- **PayloadHash comparison** = detects true collisions vs legitimate duplicates
- **LogCritical on collision** = F004 first signal
- Idempotency key = `DeduplicationId` or `EventId`

---

## D005: Circuit Breaker Implementation

**Decision:** Default thresholds (Polly defaults)

### Service Bus Circuit Breaker

**File:** `src/CloudScale.IngestionApi/Services/ServiceBusProducerService.cs`

```csharp
// Lines 51-76: Circuit breaker configuration
.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    FailureRatio = 0.5,                    // 50% failure rate triggers
    SamplingDuration = TimeSpan.FromSeconds(30),
    MinimumThroughput = 10,                // Need 10 calls before evaluating
    BreakDuration = TimeSpan.FromSeconds(30),
    ShouldHandle = new PredicateBuilder().Handle<ServiceBusException>(),
    OnOpened = args => { _logger.LogError("Circuit breaker OPENED..."); ... }
})
```

### Cosmos DB Retry (No Circuit Breaker!)

**File:** `src/CloudScale.EventProcessor/Services/CosmosDbService.cs`

```csharp
// Lines 31-60: Retry only, no circuit breaker
.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 5,
    BackoffType = DelayBackoffType.Exponential,
    ShouldHandle = new PredicateBuilder()
        .Handle<CosmosException>(ex => 
            ex.StatusCode == HttpStatusCode.TooManyRequests ||
            ex.StatusCode == HttpStatusCode.ServiceUnavailable),
    // ... uses Cosmos RetryAfter header
})
```

**Critical observation:**
- Service Bus has circuit breaker
- **Cosmos DB has NO circuit breaker** — only retry
- F001 cascade happens because retries exhaust RU without stopping

---

## D006: Single-Region Configuration

**Decision:** No multi-region support

**File:** `docker-compose.yml`

```yaml
# Single emulator instance, no replication
cosmosdb-emulator:
  image: mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator
  ports:
    - "8082:8081"
  # No geo-replication config
```

**What this means:**
- Single point of failure
- No failover path
- Production would require Azure Cosmos DB with geo-replication enabled

---

## D007: Observability Implementation

**Decision:** Full instrumentation, accept overhead

**File:** `src/CloudScale.IngestionApi/Telemetry/EventMetrics.cs`

Contains metrics counters and tracing setup.

**File:** `src/CloudScale.EventProcessor/Workers/EventProcessorWorker.cs`

```csharp
// Throughout MessageHandler: extensive logging
_logger.LogInformation("Processing event {EventId} of type {EventType}", ...);
_logger.LogWarning("Fraud detection flagged event {EventId}", ...);
_logger.LogInformation("Saved event {EventId} to Cosmos DB", ...);
```

**What this means:**
- Every event is logged multiple times
- ~5-10% overhead is the accepted cost
- F005 triggers when log sink can't keep up

---

## F001: RU Exhaustion Break Point

**Will break at:** `CosmosDbService.AddEventAsync` (Line 70)

```csharp
await _container.CreateItemAsync<dynamic>(document, ...);
```

**First signal:** Retry logs with "TooManyRequests" status

```csharp
// Line 52-56: Log shows retries
_logger.LogWarning("Cosmos retry {Attempt}/{Max} after {Delay}ms. Exception: {Message}", ...);
```

**Cascade path:**
1. CreateItemAsync returns 429
2. Retry loop burns 5 attempts
3. Each retry consumes RU (making it worse)
4. Eventually fails to DLQ
5. No circuit breaker to stop the bleeding

---

## F002: Service Bus Bottleneck Break Point

**Will break at:** `ServiceBusProducerService.PublishAsync` (Line 113)

```csharp
await _sender.SendMessageAsync(message, token);
```

**First signal:** Circuit breaker opens

```csharp
// Lines 58-62: OnOpened callback
OnOpened = args =>
{
    _logger.LogError("Circuit breaker OPENED for Service Bus. Duration: {Duration}s", ...);
}
```

**Why it breaks:**
- Emulator throughput ceiling ~4K msg/sec
- Beyond this, SendAsync latency spikes
- Circuit breaker opens (correctly)
- But ingestion API has no way to tell clients to slow down until then

---

## F003: Circuit Breaker False Positive Point

**Will trigger at:** `ServiceBusProducerService._resiliencePipeline` (Line 37)

```csharp
_resiliencePipeline = new ResiliencePipelineBuilder()
    .AddRetry(...)
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,  // <-- This threshold may be too sensitive
        MinimumThroughput = 10,
        // ...
    })
```

**Problem:** 
- 10 calls minimum → small sample size
- 50% failure ratio → 5 failures in 10 calls opens breaker
- Transient network blip could trigger this

---

## F004: Idempotency Collision Point

**Will manifest at:** `CosmosEventDocument` constructor (Line 147)

```csharp
id = @event.DeduplicationId ?? @event.EventId;
```

**Detection point:** `CosmosDbService.AddEventAsync` (Line 79-80)

```csharp
if (existing.Resource.EventData.PayloadHash != @event.PayloadHash)
{
    _logger.LogCritical("IDEMPOTENCY COLLISION: Event {EventId} received with DIFFERENT payload!");
}
```

**First signal:** `LogCritical` with "IDEMPOTENCY COLLISION"

---

## F005: Observability Overhead Point

**Will manifest at:** `EventProcessorWorker.MessageHandler` (Lines 117-214)

The entire message handling path has multiple log calls:

```csharp
_logger.LogDebug("Received message {MessageId}", args.MessageId);
_logger.LogInformation("Processing event {EventId}...", ...);
_logger.LogWarning("Fraud detection flagged...", ...);
_logger.LogInformation("Saved event...", ...);
```

**When it breaks:**
- Log sink (file, collector) can't keep up
- Logging calls become synchronously blocking
- Event processing latency spikes

---

## Quick Reference: File → Decisions/Failures

| File | D00X | F00X |
|------|------|------|
| `ServiceBusProducerService.cs` | D001, D005 | F002, F003 |
| `CosmosDbService.cs` | D003, D004, D005 | F001, F004 |
| `EventProcessorWorker.cs` | D002, D007 | F005 |
| `RateLimitingMiddleware.cs` | — | F002 (mitigation) |
| `docker-compose.yml` | D006 | — |
| `EventMetrics.cs` | D007 | F005 |
