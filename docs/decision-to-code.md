# Decision to Code Mapping

This document maps each architectural decision to its concrete code enforcement points. If a decision cannot be traced to code, it is not a decision — it is a wish.

---

## D001 – Azure Service Bus over Apache Kafka

**Intent:** Use Azure Service Bus for event messaging to reduce operational complexity at the cost of throughput ceiling and vendor lock-in.

### Code Anchors

- `/src/CloudScale.IngestionApi/Services/ServiceBusProducerService.cs` — Producer implementation
- `/src/CloudScale.EventProcessor/Workers/EventProcessorWorker.cs:L25-35` — Consumer initialization
- `/ServiceBusConfig.json` — Queue/topic configuration
- `/docker-compose.yml:L45-60` — Service Bus Emulator container

### Enforced By

- `ServiceBusClient` dependency injection (no Kafka alternative exists)
- `ServiceBusSender.SendMessageAsync()` — Azure-specific API
- Emulator throughput ceiling: ~4K msg/sec (hardware bound)

### Backpressure Strategy

**Implemented — not theoretical.**

| Component | Mechanism | Code Location |
|-----------|-----------|---------------|
| Ingress Rate Limiting | Token Bucket + Sliding Window | `RateLimitingMiddleware.cs:L34-41` |
| Queue Depth Monitor | Threshold-based concurrency reduction | `BackpressureMonitor.cs:L24-27` |
| API Throttling Signal | Cosmos-backed health flag | `SystemHealthWatcher.cs:L49-58` |
| Hard Cap | Global: 10K/min, Per-IP: 100 burst | `appsettings.json:RateLimiting` |
| Drop Policy | 429 with Retry-After header | `RateLimitingMiddleware.cs:L113-127` |

**Propagation path:**
1. `BackpressureMonitor` detects queue depth > threshold
2. Writes `IsUnderPressure=true` to Cosmos via `UpdateSystemHealthAsync()`
3. `SystemHealthWatcher` polls Cosmos, sets `IsThrottlingEnabled=true`
4. API can check `ISystemHealthProvider.IsThrottlingEnabled` before accepting events

### Failure Link

- **F002** – Service Bus Throughput Ceiling

### Notes

- Switching to Kafka requires: new producer service, new consumer worker, Zookeeper/KRaft infra
- Estimated migration effort: 2-3 weeks for a small team
- This decision is **locked** until throughput exceeds 10K events/sec

---

## D002 – CQRS with Eventual Consistency

**Intent:** Separate read and write paths to isolate write throughput from read load, accepting temporary inconsistency.

### Code Anchors

- `/src/CloudScale.EventProcessor/Workers/EventProcessorWorker.cs:L117-214` — Write path
- `/src/CloudScale.EventProcessor/Workers/ReadModelProjectorWorker.cs` — Read model projection
- `/src/CloudScale.IngestionApi/Endpoints/ReadEndpoints.cs` — Read path (queries)
- `/src/CloudScale.EventProcessor/Services/CosmosDbService.cs:L63-91` — Write to event store

### Enforced By

- Write path: `EventProcessorWorker.MessageHandler()` → `CosmosDbService.AddEventAsync()`
- Read path: `ReadEndpoints` → separate query models
- No shared transaction scope between write and read
- Read models updated asynchronously via `ReadModelProjectorWorker`

### Failure Link

- No direct failure scenario — design choice, not failure point
- Indirect: stale reads may confuse users if consistency gap > 5 seconds

### Notes

- Read lag is inherent, not a bug
- Dashboard queries should display "last updated" timestamp

---

## D003a – Time-Based Partition Key Selection

**Intent:** Partition by `{TenantId}:{yyyy-MM}` to optimize recent-event queries.

### Code Anchors

- `/src/CloudScale.EventProcessor/Services/CosmosDbService.cs:L145-153` — Partition key construction

```csharp
// Line 151: Partition key formula
PartitionKey = $"{@event.TenantId}:{@event.CreatedAt:yyyy-MM}";
```

### Enforced By

- `CosmosEventDocument` constructor — every document gets this key
- Cosmos DB container configured with `/PartitionKey` path

### Failure Link

- **F001** – RU Exhaustion (hot partition during traffic spike)

### Notes

- Partition key = `{TenantId}:{yyyy-MM}` (e.g., `tenant1:2026-02`)
- Monthly bucket means all events in a month share partition
- Changing partition key requires data migration

---

## D003b – Hot Partition Mitigation

**Intent:** Detect and respond to partition saturation before cascade.

### Code Anchors

| Mechanism | Status | Location |
|-----------|--------|----------|
| Queue depth monitoring | ✅ Implemented | `BackpressureMonitor.cs:L24-27` |
| Concurrency reduction | ✅ Implemented | `BackpressureMonitor.cs:L92-119` |
| RU-aware backoff | ✅ Implemented | `CosmosDbService.cs:L44-49` (RetryAfter header) |
| Hot partition detection | ❌ NOT IMPLEMENTED | — |
| Partition-level circuit breaker | ❌ NOT IMPLEMENTED | — |
| Time-bucket randomization | ❌ NOT IMPLEMENTED | — |

### Explicitly Unimplemented

The following mitigation strategies are **known gaps**:

```
❌ Per-partition RU monitoring
   - Cannot detect which partition is hot
   - All 429s treated equally

❌ Write shedding by partition
   - Cannot selectively drop events for hot partition
   - All-or-nothing throttling

❌ Partition suffix randomization
   - Would spread load: {TenantId}:{yyyy-MM}:{random(0-N)}
   - Not implemented due to query complexity increase
```

### Failure Link

- **F001** – RU Exhaustion + DLQ Saturation

### Runtime Protection Status

| Control | Status |
|---------|--------|
| Cosmos RetryAfter respected | ✅ Yes |
| Concurrency auto-reduction | ✅ Yes (via BackpressureMonitor) |
| Per-partition CB | ❌ No |
| Hot partition alert | ❌ No |

### Notes

- **This is the most critical unprotected area in the system**
- F001 cascade is possible because partition-level visibility is missing
- Mitigation requires Azure Monitor Cosmos metrics integration

---

## D004 – Idempotency at Consumer Level

**Intent:** Broker provides at-least-once delivery; consumers handle idempotency via deduplication ID and payload hash comparison.

### Code Anchors

- `/src/CloudScale.EventProcessor/Services/CosmosDbService.cs:L63-91` — Idempotency enforcement
- `/src/CloudScale.EventProcessor/Services/CosmosDbService.cs:L147` — Deduplication ID assignment

```csharp
// Line 147: ID assignment
id = @event.DeduplicationId ?? @event.EventId;

// Lines 73-88: Conflict handling with payload comparison
catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
{
    var existing = await _container.ReadItemAsync<CosmosEventDocument>(...);
    if (existing.Resource.EventData.PayloadHash != @event.PayloadHash)
    {
        _logger.LogCritical("IDEMPOTENCY COLLISION: ...");
    }
}
```

### Enforced By

- Cosmos DB 409 Conflict on duplicate `id`
- `PayloadHash` comparison distinguishes collision from legitimate retry
- `LogCritical` on collision = F004 first signal

### Failure Link

- **F004** – Idempotency Key Collision

### Notes

- Collision detection relies on `PayloadHash`
- Event IDs must have sufficient entropy (UUID v4)

---

## D005 – Circuit Breaker Strategy

**Intent:** Use Polly circuit breakers to fail fast. Currently partial implementation.

### Code Anchors

**Service Bus (PROTECTED):**
- `/src/CloudScale.IngestionApi/Services/ServiceBusProducerService.cs:L51-76`

```csharp
.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    FailureRatio = 0.5,           // 50% failures trigger
    SamplingDuration = TimeSpan.FromSeconds(30),
    MinimumThroughput = 10,
    BreakDuration = TimeSpan.FromSeconds(30),
})
```

**Cosmos DB (NOT PROTECTED):**
- `/src/CloudScale.EventProcessor/Services/CosmosDbService.cs:L31-60`

```csharp
// Retry only — NO circuit breaker
.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 5, ... })
```

### Explicitly Unprotected

```md
### ⚠️ Known Cascading Risk

Cosmos DB write path has NO circuit breaker.

Consequence:
- 429 RU exhaustion triggers 5 retries
- Each retry consumes more RU
- No fail-fast mechanism
- F001 cascade accelerates instead of stopping

This gap is KNOWN and ACCEPTED for now.
Production deployment MUST add Cosmos circuit breaker.
```

### Failure Links

- **F001** – RU Exhaustion (Cosmos retries without CB accelerate cascade)
- **F003** – Circuit Breaker False Positive (Service Bus CB may trip incorrectly)

### Runtime Config Boundaries

| Setting | Hardcoded? | Location |
|---------|------------|----------|
| CB FailureRatio | Yes (0.5) | ServiceBusProducerService.cs:L52 |
| CB BreakDuration | Yes (30s) | ServiceBusProducerService.cs:L55 |
| Cosmos MaxRetry | Yes (5) | CosmosDbService.cs:L34 |

Cannot be changed at runtime without redeployment.

---

## D006 – No Multi-Region Support

**Intent:** Single-region deployment only. No geo-redundancy.

### Code Anchors

- `/docker-compose.yml` — Single emulator instance
- No multi-region config in `/infra/`

### Enforced By

- Connection strings point to single endpoint
- No failover logic exists

### Failure Link

- Entire system is single point of failure

---

## D007 – Observability over Raw Throughput

**Intent:** Instrument everything, accept overhead.

### Code Anchors

- `/src/CloudScale.IngestionApi/Telemetry/EventMetrics.cs` — Metrics
- `/src/CloudScale.EventProcessor/Workers/EventProcessorWorker.cs` — Extensive logging
- `/src/CloudScale.Shared/Telemetry/TelemetryConstants.cs` — Metric names

### Enforced By

- `ILogger` in every service
- No sampling — 100% trace rate
- Structured logging with correlation IDs

### Failure Link

- **F005** – Observability Overhead

---

## Summary Matrix

| Decision | Circuit Breaker | Backpressure | Hot Partition Guard | Failure Link |
|----------|-----------------|--------------|---------------------|--------------|
| D001 | ✅ Service Bus | ✅ Full chain | — | F002 |
| D002 | — | — | — | — |
| D003a | — | — | ❌ | F001 |
| D003b | ❌ Cosmos unprotected | ⚠️ Queue-level only | ❌ | F001 |
| D004 | — | — | — | F004 |
| D005 | ⚠️ Partial | — | — | F001, F003 |
| D006 | — | — | — | (all) |
| D007 | — | — | — | F005 |
