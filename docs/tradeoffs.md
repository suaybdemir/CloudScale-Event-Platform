# Technical Trade-offs & Decision Rationale

![System Topology](images/system_architecture_diagram.png)

> **"Every architecture is a set of trade-offs. This document explains why we made each choice."**

This document is designed for technical interviews. Each section answers: **What did we choose? What were the alternatives? Why this decision?**

---

## 1. Message Broker: Service Bus vs Event Hubs

### Decision: Azure Service Bus (Standard Tier)

| Factor | Service Bus ✅ | Event Hubs ❌ |
|--------|---------------|--------------|
| Dead Letter Queue | Native DLQ with reason codes | Manual implementation |
| Message Ordering | Per-session FIFO | Per-partition only |
| Message Locking | Lock + renew pattern | Consumer offset |
| Throughput | ~10k msg/sec | Millions msg/sec |
| Pricing | Per-message | Per-throughput unit |

### Why Service Bus?
1. **DLQ is critical**: Poison messages must not block processing
2. **We don't need millions/sec**: 10k events/min is well within capacity
3. **Session support**: Future option for per-tenant ordering

### When to switch to Event Hubs:
- Need log-style replay (event sourcing)
- Throughput > 100k events/sec
- Integration with Stream Analytics

---

## 2. Database: Cosmos DB vs PostgreSQL

### Decision: Azure Cosmos DB (NoSQL API)

| Factor | Cosmos DB ✅ | PostgreSQL ❌ |
|--------|-------------|--------------|
| Write Latency | 5-10ms | 20-50ms |
| Schema | Flexible (JSON) | Rigid (migrations) |
| Scaling | Auto-scale RU | Manual sharding |
| Global Distribution | Built-in | Complex setup |

### Why Cosmos DB?
1. **Latency SLO**: Need sub-10ms writes for real-time feel
2. **Event schema varies**: Different event types, flexible metadata
3. **Multi-tenant partitioning**: Native partition key support

### Trade-offs accepted:
- Higher cost per operation
- No complex JOINs
- RU-based pricing can be unpredictable

### Mitigation:
- TTL for automatic cleanup (cost control)
- Proper partition key reduces cross-partition queries
- Autoscale prevents over-provisioning

---

## 3. Compute: Container Apps vs AKS vs Functions

### Decision: Azure Container Apps

| Factor | Container Apps ✅ | AKS ❌ | Functions ❌ |
|--------|------------------|--------|-------------|
| Ops Complexity | Zero | High (cluster mgmt) | Zero |
| Cold Start | Minimal | None | 1-10+ seconds |
| Scaling | KEDA built-in | KEDA install | Built-in |
| Cost | Pay-per-use | Fixed cluster | Per-execution |
| Long-running | Supported | Supported | 10 min limit |

### Why Container Apps?
1. **No Kubernetes expertise required**: Focus on application, not infrastructure
2. **KEDA auto-scaling**: Native Service Bus trigger
3. **Cost-effective**: No idle cluster costs

### Why NOT AKS (yet)?
- No need for custom operators
- No multi-cluster requirements
- Team doesn't have K8s expertise

### Why NOT Functions?
- Cold start unacceptable for real-time processing
- Execution time limits for complex events
- State management (fraud detection cache) is awkward

---

## 4. API Gateway: Custom vs Azure API Management

### Decision: Nginx + Custom Rate Limiting Middleware

| Factor | Custom ✅ | APIM ❌ |
|--------|----------|--------|
| Monthly Cost | ~$0 | $50+ (Developer tier) |
| Rate Limiting | Token Bucket + Sliding Window | Policy-based |
| Customization | Full control | Configuration limits |
| Developer Portal | None | Built-in |
| OAuth/Subscriptions | Manual | Built-in |

### Why Custom?
1. **Cost**: MVP doesn't need $50/month overhead
2. **Algorithm control**: Implemented both Token Bucket and Sliding Window
3. **Interview talking point**: "I implemented rate limiting from scratch"

### When to migrate to APIM:
- Multiple external consumers
- Need subscription key management
- OAuth integration required
- Want built-in caching

---

## 5. Consistency Model: Session vs Strong vs Eventual

### Decision: Session Consistency (Cosmos DB default)

| Level | Latency | Guarantee | Use Case |
|-------|---------|-----------|----------|
| Strong | Highest | Linearizable | Banking |
| Bounded Staleness | Medium | Time-bounded | Trading |
| Session ✅ | Low | Read-your-writes | User apps |
| Consistent Prefix | Lower | Ordered | Analytics |
| Eventual | Lowest | None | Logging |

### Why Session?
1. **Read-your-writes**: Users see their own events immediately
2. **Good enough**: We don't need linearizable for event storage
3. **Multi-region compatible**: Works with geo-replication

### Trade-off:
- Other users might see slightly stale data
- Acceptable for event analytics use case

---

## 6. Rate Limiting Algorithm: Why Two Algorithms?

### Decision: Token Bucket (per-IP) + Sliding Window (global)

### Token Bucket (Per-IP)
```
Allows: Legitimate bursts (batch upload)
Prevents: Single-source abuse
Config: 100 tokens, 10/sec refill
```

### Sliding Window (Global)
```
Allows: Distributed traffic
Prevents: System overload from many sources
Config: 10,000 requests/minute
```

### Why Both?
| Attack Type | Token Bucket | Sliding Window |
|-------------|--------------|----------------|
| Single IP flooding | ✅ Blocks | Passes (if under global) |
| DDoS (many IPs) | Passes (per IP OK) | ✅ Blocks total |
| Legitimate burst | ✅ Allows | ✅ Allows if under limit |

**Defense in depth**: Neither algorithm alone covers all cases.

---

## 7. Partition Key: TenantId:yyyy-MM

### Decision: Composite key with tenant + month

```
Format: {TenantId}:{yyyy-MM}
Example: acme:2026-01
```

### Alternatives Considered

| Strategy | Pros | Cons |
|----------|------|------|
| TenantId only | Simple | Hot partition in active months |
| EventId | Perfect distribution | No logical grouping |
| yyyy-MM only | Time-based | Cross-tenant queries expensive |
| TenantId:yyyy-MM ✅ | Balanced | Cross-month queries need fan-out |

### Why This Strategy?
1. **Tenant isolation**: Most queries filter by tenant (single partition)
2. **Time distribution**: Spreads writes across months
3. **TTL alignment**: Old partitions expire together
4. **Predictable performance**: Query scope is clear

### Trade-off accepted:
- Queries spanning multiple months require fan-out
- Acceptable because most queries are recent data

---

## 8. Observability: OpenTelemetry vs Proprietary SDKs

### Decision: OpenTelemetry with Azure Monitor export

| Approach | Vendor Lock-in | Flexibility | Ecosystem |
|----------|----------------|-------------|-----------|
| App Insights SDK only | High | Low | Azure only |
| OpenTelemetry ✅ | None | High | Universal |

### Why OpenTelemetry?
1. **Vendor neutral**: Can export to Jaeger, Prometheus, etc.
2. **Industry standard**: W3C Trace Context support
3. **Future-proof**: Easy to change backends

### Azure Monitor integration:
- Still use App Insights for Azure-native dashboards
- `Azure.Monitor.OpenTelemetry.Exporter` bridges the gap
- Best of both worlds

---

## 9. Retry Strategy: Polly Configuration

### Decision: Exponential Backoff + Circuit Breaker

```csharp
// Service Bus Producer
Retry: 3 attempts, 500ms → 1s → 2s (exponential + jitter)
Circuit Breaker: 50% failure rate in 30s → Open for 30s

// Cosmos DB
Retry: 5 attempts, 200ms base + respect RetryAfter header
```

### Why These Numbers?
| Setting | Value | Rationale |
|---------|-------|-----------|
| Max retries | 3-5 | Enough for transient, not infinite loop |
| Base delay | 200-500ms | Quick recovery for hiccups |
| Backoff | Exponential | Prevents thundering herd |
| Jitter | Random | Spreads retry load |
| Circuit threshold | 50% | Opens before total failure |
| Break duration | 30s | Time for downstream recovery |

---

## 10. What We Chose NOT to Build

| Feature | Reason |
|---------|--------|
| Event Sourcing | Complexity > benefit for this use case |
| CQRS | Single Cosmos container is sufficient |
| Saga Pattern | No multi-step transactions needed |
| API Versioning | Single consumer (internal dashboard) |
| Feature Flags | Adds infrastructure complexity |
| Real-time WebSocket | Dashboard polls every few seconds |

**Philosophy**: Build what's needed, document what's deferred.

---

## Summary: Senior vs Junior Decisions

| Junior Approach | Our Senior Approach |
|-----------------|---------------------|
| "Use Kafka because it's popular" | Service Bus because DLQ matters |
| "Use AKS for credibility" | Container Apps: right-sized |
| "Strong consistency everywhere" | Session: balanced trade-off |
| "Single rate limiting" | Two algorithms for defense in depth |
| "Copy-paste retry logic" | Polly with tuned parameters |
| "Build everything" | Know what NOT to build |
