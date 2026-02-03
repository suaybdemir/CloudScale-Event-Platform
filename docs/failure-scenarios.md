# CloudScale Event Platform — Failure Scenarios

This document catalogs known failure modes, their detection signals, cascade effects, and response playbooks. Each scenario follows a strict structure to ensure operational readiness.

---

## How to Use This Document

- **During incidents:** Jump to the matching scenario, follow Escape Hatch.
- **Post-incident:** Verify if scenario was accurate, update if not.
- **Pre-deployment:** Review all scenarios, confirm Prevention Status.

---

## F001: Cosmos DB RU Exhaustion + Dead Letter Queue Saturation

### Trigger

| Type | Description |
|------|-------------|
| **Technical** | Traffic spike exceeds provisioned RU capacity |
| **Operational** | RU budget not adjusted before anticipated high-traffic period |
| **Human** | Alert fatigue — previous RU warnings ignored |

### Code Breakpoints

| File | Line | What Breaks | Why Here |
|------|------|-------------|----------|
| `CosmosDbService.cs` | L70 | `CreateItemAsync()` returns 429 | First point of RU rejection |
| `CosmosDbService.cs` | L34 | `MaxRetryAttempts = 5` | Each retry burns more RU |
| `CosmosDbService.cs` | L52-56 | Retry log fires | First observable signal in logs |
| `EventProcessorWorker.cs` | L195 | Message abandoned to DLQ | Retry budget exhausted |

**First log line you'll see:**
```
[WRN] Cosmos retry 1/5 after 200ms. Exception: TooManyRequests
```

**What triggers 429:**
- Partition RU budget exceeded
- No per-partition visibility → cannot tell which partition is hot
- `PartitionKey = $"{TenantId}:{yyyy-MM}"` → all monthly traffic hits same partition

### First Signal

| Metric | Location | Delay |
|--------|----------|-------|
| `cosmos_throttled_requests` | Grafana / Cosmos Metrics | ~30 seconds |
| `servicebus_dlq_depth` | Service Bus dashboard | ~2-5 minutes |
| `consumer_retry_count` | Application logs | ~1 minute |

**Warning:** DLQ depth lags behind actual failure. By the time DLQ alerts fire, backlog may already be significant.

### Cascade

1. Cosmos DB throttles writes (429 responses)
2. Event Processor retries → each retry consumes more RU
3. Retry budget exhausted → messages moved to Dead Letter Queue
4. DLQ fills silently (no active consumer)
5. If not caught: events stuck indefinitely, effective data loss

**Decision Link:** This cascade is enabled by D003 (time-based partitioning creates hot partition risk) and D005 (no circuit breaker on Cosmos, only retry).

**Why no circuit breaker stops this:**
```csharp
// CosmosDbService.cs:L31-60 — Retry only, NO circuit breaker
.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 5, ... })
// ← Missing: .AddCircuitBreaker(...)
```

### Blast Radius

| Dimension | Impact |
|-----------|--------|
| Events affected | Potentially thousands during spike window |
| Consumers affected | All consumers writing to Cosmos |
| Data impact | **Delay** initially, **loss** if DLQ not drained |

### Human Response

**Correct sequence:**
1. Check `cosmos_throttled_requests` — confirm RU exhaustion
2. Increase provisioned RU (Azure Portal or CLI)
3. Pause Event Processor temporarily to stop retry storm
4. Drain DLQ manually once RU stabilized
5. Resume processor with reduced batch size

**Wrong moves:**
- Restarting processor without fixing RU → accelerates cascade
- Ignoring DLQ → silent data loss
- Increasing RU without pausing processor → RU consumed by retries, not new events

### Escape Hatch

| Action | Type | Notes |
|--------|------|-------|
| Scale up RU | Temporary | Buys time, doesn't fix root cause |
| Pause processor | Temporary | Stops cascade, requires manual restart |
| Drain DLQ | Manual | Must be done before messages expire |

**Permanent fix:** Implement adaptive RU scaling or pre-provision for known peak hours.

### Prevention Status

| Control | Status |
|---------|--------|
| RU auto-scaling | ❌ Not implemented |
| DLQ alerting | ⚠️ Basic (threshold not tuned) |
| Circuit breaker for Cosmos | ❌ **Not implemented** (D005 gap) |
| Chaos testing | ❌ Not done |

---

## F002: Service Bus Emulator Throughput Ceiling

### Trigger

| Type | Description |
|------|-------------|
| **Technical** | Ingestion rate exceeds ~4K msg/sec emulator limit |
| **Operational** | Load test without throttling on ingress |
| **Human** | Assumed emulator = production parity |

### Code Breakpoints

| File | Line | What Breaks | Why Here |
|------|------|-------------|----------|
| `ServiceBusProducerService.cs` | L113 | `SendMessageAsync()` hangs | Emulator throughput saturated |
| `ServiceBusProducerService.cs` | L58-62 | Circuit breaker opens | CB detects 50% failure rate |
| `RateLimitingMiddleware.cs` | L56-60 | Global limit hit | Ingress shedding kicks in |
| `EventEndpoints.cs` | — | Returns 503 | Thread pool exhaustion |

**First log line you'll see:**
```
[ERR] Circuit breaker OPENED for Service Bus. Duration: 30s
```

**What happens before logs:**
- `SendAsync` latency spikes (> 10 seconds)
- Kestrel thread pool backs up
- No log until circuit breaker trips

### First Signal

| Metric | Location | Delay |
|--------|----------|-------|
| `servicebus_send_latency_p99` | Application metrics | ~10 seconds |
| `ingestion_api_5xx_rate` | Nginx / API logs | ~30 seconds |
| Kestrel thread pool exhaustion | dotnet-counters | ~1 minute |

### Cascade

1. Service Bus `SendAsync` latency spikes (>10s)
2. Kestrel thread pool backs up
3. Ingestion API starts returning 503
4. Clients retry → amplifies load
5. System enters overload spiral

**Decision Link:** D001 (chose Service Bus for simplicity, accepted lower throughput ceiling).

### Blast Radius

| Dimension | Impact |
|-----------|--------|
| Events affected | All incoming events during overload |
| Consumers affected | None directly — they starve instead |
| Data impact | **Rejected at ingress** — no silent loss, but clients see errors |

### Human Response

**Correct sequence:**
1. Enable ingress rate limiting (Nginx or API-level)
2. Reduce client send rate if controllable
3. Wait for queue to drain
4. Resume normal rate below 4K/sec or deploy to Azure (not emulator)

**Wrong moves:**
- Increasing API replicas → doesn't help, bottleneck is Service Bus
- Restarting Service Bus emulator → loses in-flight messages

### Escape Hatch

| Action | Type | Notes |
|--------|------|-------|
| Rate limit ingress | Temporary | Sheds load at edge |
| Client backoff | Temporary | Requires client cooperation |
| Deploy to Azure Service Bus | Permanent | Removes emulator ceiling |

### Prevention Status

| Control | Status |
|---------|--------|
| Ingress rate limiting | ✅ Implemented (RateLimitingMiddleware) |
| Throughput dashboard | ✅ Exists |
| Load testing | ⚠️ Done up to 5K, not beyond |

---

## F003: Circuit Breaker False Positive (Premature Open)

### Trigger

| Type | Description |
|------|-------------|
| **Technical** | Transient network blip or GC pause |
| **Operational** | Deployment during high traffic |
| **Human** | Breaker thresholds set too aggressively |

### Code Breakpoints

| File | Line | What Breaks | Why Here |
|------|------|-------------|----------|
| `ServiceBusProducerService.cs` | L52 | `FailureRatio = 0.5` | Threshold too sensitive |
| `ServiceBusProducerService.cs` | L54 | `MinimumThroughput = 10` | Small sample size |
| `ServiceBusProducerService.cs` | L58-62 | `OnOpened` callback | Breaker opens |

**Why false positive happens:**
```csharp
// ServiceBusProducerService.cs:L51-56
.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    FailureRatio = 0.5,        // 5 failures in 10 calls → open
    MinimumThroughput = 10,    // Only 10 calls needed to evaluate
    SamplingDuration = TimeSpan.FromSeconds(30),
})
```

**Scenario:**
- 10 requests come in
- 5 hit a GC pause → timeout
- Breaker opens for 30 seconds
- All subsequent requests fail immediately

**First log line you'll see:**
```
[ERR] Circuit breaker OPENED for Service Bus. Duration: 30s
```

### First Signal

| Metric | Location | Delay |
|--------|----------|-------|
| `circuit_breaker_state` = Open | Application logs | Immediate |
| `consumer_processing_rate` drops to 0 | Grafana | ~10 seconds |
| Queue depth rising | Service Bus metrics | ~1 minute |

### Cascade

1. Breaker opens on transient failure
2. All processing stops for breaker timeout duration
3. Queue depth increases
4. When breaker closes, burst of retries may re-trip it
5. System oscillates between open/closed

**Decision Link:** D005 (default thresholds not tuned to actual failure patterns).

### Blast Radius

| Dimension | Impact |
|-----------|--------|
| Events affected | All events during breaker-open window |
| Consumers affected | All consumers behind the breaker |
| Data impact | **Delay only** — events queue, not lost |

### Human Response

**Correct sequence:**
1. Check if downstream (Cosmos) is actually down
2. If healthy: manually close breaker or restart processor
3. If unhealthy: treat as F001
4. Post-incident: tune thresholds based on this event

**Wrong moves:**
- Disabling circuit breaker entirely → removes protection for real failures
- Ignoring oscillation → system remains unstable

### Escape Hatch

| Action | Type | Notes |
|--------|------|-------|
| Manual breaker reset | Temporary | Restores processing |
| Reduce batch size | Temporary | Lowers failure surface |
| Tune thresholds | Permanent | Requires failure data |

### Prevention Status

| Control | Status |
|---------|--------|
| Threshold tuning | ❌ Not done (D005) |
| Breaker state alerting | ⚠️ Logs only, no dashboard |
| Chaos testing | ❌ Not done |

---

## F004: Idempotency Key Collision

### Trigger

| Type | Description |
|------|-------------|
| **Technical** | Event ID generation collision (unlikely but possible) |
| **Operational** | Replay of old events with same IDs |
| **Human** | Developer reuses event IDs in testing |

### Code Breakpoints

| File | Line | What Breaks | Why Here |
|------|------|-------------|----------|
| `CosmosDbService.cs` | L147 | `id = DeduplicationId ?? EventId` | ID assignment point |
| `CosmosDbService.cs` | L73 | `Conflict` exception caught | Duplicate detected |
| `CosmosDbService.cs` | L79-80 | `PayloadHash` comparison | Collision vs retry check |
| `CosmosDbService.cs` | L81 | `LogCritical` fires | **THIS IS YOUR SIGNAL** |

**Detection code:**
```csharp
// CosmosDbService.cs:L73-88
catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
{
    var existing = await _container.ReadItemAsync<CosmosEventDocument>(...);
    
    if (existing.Resource.EventData.PayloadHash != @event.PayloadHash)
    {
        _logger.LogCritical("IDEMPOTENCY COLLISION: Event {EventId} received with DIFFERENT payload!");
        // ← THIS LOG MEANS REAL DATA LOSS
    }
    else
    {
        _logger.LogWarning("Duplicate event {EventId} with identical payload (ignored)");
        // ← This is fine, legitimate retry
    }
}
```

**First log line for collision:**
```
[CRT] IDEMPOTENCY COLLISION: Event abc123 received with DIFFERENT payload! Potential Replay/Tampering Attack.
```

**First log line for legitimate duplicate:**
```
[WRN] Duplicate event abc123 with identical payload (ignored)
```

### First Signal

| Metric | Location | Delay |
|--------|----------|-------|
| `LogCritical` with "COLLISION" | Application logs | Immediate |
| Data anomalies | Dashboard / user reports | Hours to days |

### Cascade

1. Consumer sees "already processed" ID
2. Event silently dropped (idempotency working as designed)
3. But if IDs collided incorrectly → real event lost
4. No immediate error — data inconsistency discovered later

**Decision Link:** D004 (idempotency at consumer level, not broker-guaranteed).

### Blast Radius

| Dimension | Impact |
|-----------|--------|
| Events affected | Individual colliding events |
| Consumers affected | Consumer with collision |
| Data impact | **Silent loss** — worst kind |

### Human Response

**Correct sequence:**
1. Investigate `LogCritical` with "COLLISION" in logs
2. Check if duplicates are legitimate (retries) or collisions
3. If collision: audit ID generation logic
4. Replay affected events with corrected IDs

**Wrong moves:**
- Ignoring `LogCritical` → misses collisions
- Disabling idempotency check → allows actual duplicates through

### Escape Hatch

| Action | Type | Notes |
|--------|------|-------|
| Audit ID generator | Investigation | Check for entropy issues |
| Manual event replay | Recovery | Requires identifying affected events |

### Prevention Status

| Control | Status |
|---------|--------|
| UUID generation (proper entropy) | ✅ Using standard library |
| Collision detection | ✅ PayloadHash comparison |
| Alert on LogCritical | ⚠️ Exists, not alerted |
| ID collision testing | ❌ Not done |

---

## F005: Observability Overhead Causes Latency Spike

### Trigger

| Type | Description |
|------|-------------|
| **Technical** | Trace exporter backpressure or log sink full |
| **Operational** | Observability infra under-provisioned for load |
| **Human** | Added verbose logging without measuring impact |

### Code Breakpoints

| File | Line | What Breaks | Why Here |
|------|------|-------------|----------|
| `EventProcessorWorker.cs` | L120+ | Multiple `_logger.Log*` calls | Logging blocks thread |
| `EventProcessorWorker.cs` | L175 | `LogInformation("Saved event...")` | Per-event log |
| `CosmosDbService.cs` | L71 | `LogInformation("Saved event...")` | Duplicate logging |

**Why it breaks:**
```csharp
// EventProcessorWorker.cs — MessageHandler has 5+ log calls per event
_logger.LogDebug("Received message {MessageId}", args.MessageId);
_logger.LogInformation("Processing event {EventId}...", ...);
_logger.LogWarning("Fraud detection flagged...", ...);  // conditional
_logger.LogInformation("Saved event...", ...);
// Each call blocks if sink is slow
```

**First log line you'll see:**
```
[WRN] Log buffer full, dropping entries
```
(from log sink, not application)

### First Signal

| Metric | Location | Delay |
|--------|----------|-------|
| `trace_export_latency` | OTEL collector | Immediate |
| `event_processing_p99` spike | Application metrics | ~30 seconds |
| Log ingestion lag | Log aggregator | ~1 minute |

### Cascade

1. Trace export becomes slow
2. Tracing calls block application threads
3. Event processing latency increases
4. Backpressure builds from ingestion layer
5. System slowdown across all components

**Decision Link:** D007 (observability over throughput, accepted overhead).

### Blast Radius

| Dimension | Impact |
|-----------|--------|
| Events affected | All events (latency increase) |
| Consumers affected | All components with tracing |
| Data impact | **Delay only** — no loss |

### Human Response

**Correct sequence:**
1. Check observability infra health (collector, sink)
2. If overwhelmed: reduce trace sampling temporarily
3. Scale observability infra
4. Resume full tracing

**Wrong moves:**
- Disabling tracing entirely → loses debugging capability
- Ignoring latency spike → cascades to timeouts

### Escape Hatch

| Action | Type | Notes |
|--------|------|-------|
| Reduce sampling rate | Temporary | 10% sampling during crisis |
| Scale collector | Permanent | Match to traffic growth |

### Prevention Status

| Control | Status |
|---------|--------|
| Collector capacity monitoring | ⚠️ Basic |
| Dynamic sampling | ❌ Not implemented |
| Load testing with tracing | ⚠️ Partial |

---

## Scenario Summary Matrix

| ID | Scenario | Severity | Detection Speed | Data Risk | Prevention |
|----|----------|----------|-----------------|-----------|------------|
| F001 | RU Exhaustion + DLQ | 🔴 High | Medium | Loss possible | ❌ Weak |
| F002 | Service Bus Ceiling | 🟡 Medium | Fast | Rejection only | ✅ Mitigated |
| F003 | CB False Positive | 🟡 Medium | Fast | Delay only | ❌ Weak |
| F004 | Idempotency Collision | 🔴 High | Slow | Silent loss | ⚠️ Partial |
| F005 | Observability Overhead | 🟢 Low | Fast | Delay only | ⚠️ Partial |

---

## Cross-Reference to Decisions

| Scenario | Enabled By | Mitigated By | Primary Code Location |
|----------|------------|--------------|----------------------|
| F001 | D003a, D003b, D005 | D007 (observability) | CosmosDbService.cs:L70 |
| F002 | D001 | RateLimitingMiddleware | ServiceBusProducerService.cs:L113 |
| F003 | D005 | D007 (breaker state logged) | ServiceBusProducerService.cs:L52 |
| F004 | D004 | PayloadHash check | CosmosDbService.cs:L79-81 |
| F005 | D007 | (trade-off itself) | EventProcessorWorker.cs:L120+ |
