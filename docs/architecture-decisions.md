# Architecture Decision Records (ADR)

![System Context](images/system_architecture_diagram.png)

This document captures key architectural decisions for the CloudScale Event Intelligence Platform.

---

## ADR-001: Azure API Management Deferred

**Status**: Deferred to Phase 2

**Context**: 
Azure API Management (APIM) provides enterprise API gateway features including:
- Rate limiting / throttling
- OAuth / subscription key management  
- Request/response transformation
- Developer portal
- Advanced caching

**Decision**: Use Nginx + custom rate limiting middleware for MVP.

**Rationale**:
1. APIM adds ~$50+/month baseline cost (Developer tier)
2. Custom `RateLimitingMiddleware` provides Token Bucket + Sliding Window algorithms
3. Nginx handles load balancing across API replicas
4. Current requirements don't need developer portal or OAuth flows

**When to migrate to APIM**:
- Multiple external API consumers
- Need for subscription key management
- Complex caching requirements
- B2B integration scenarios

**Consequences**:
- ✅ Rate limiting implemented in `RateLimitingMiddleware.cs`
- ✅ Lower operational cost
- ⚠️ No built-in developer portal
- ⚠️ Manual implementation of advanced features

---

## ADR-002: Container Apps over AKS

**Status**: Accepted

**Context**: 
Need container orchestration for Event Processor horizontal scaling.

**Options Considered**:
| Option | Pros | Cons |
|--------|------|------|
| Azure Kubernetes Service (AKS) | Full K8s control, custom operators | Operational complexity, fixed cost |
| Azure Container Apps | Serverless, KEDA built-in | Less control, newer service |
| Azure Functions | Event-driven scaling | Cold start latency, execution limits |

**Decision**: Azure Container Apps

**Rationale**:
1. **No K8s operational overhead** - No cluster management, node patching
2. **Built-in KEDA scaling** - Native Service Bus scaling trigger
3. **Pay-per-use pricing** - No idle cluster costs
4. **Sufficient for 10k+ events/min** - Tested and validated

**When to reconsider AKS**:
- Need custom Kubernetes operators
- Multi-cluster federation required
- Cold start time < 1s critical
- Sidecar injection requirements

---

## ADR-003: Service Bus over Event Hubs

**Status**: Accepted

**Context**: 
Need reliable message broker for event pipeline.

**Comparison**:
| Feature | Service Bus | Event Hubs |
|---------|-------------|------------|
| Ordering | Per-session guaranteed | Per-partition |
| Delivery | At-least-once with DLQ | At-least-once |
| Retention | 14 days max | 7 days (90 with capture) |
| Throughput | ~10k msg/sec | Millions msg/sec |
| Pricing | Per-message + base | Per-throughput unit |

**Decision**: Azure Service Bus (Standard tier)

**Rationale**:
1. **Dead Letter Queue (DLQ)** - Critical for poison message handling
2. **Session support** - Can enable per-tenant ordering if needed
3. **Simpler scaling** - Queue partitions automatic
4. **Sufficient throughput** - 10k events/min well within capacity

**When to switch to Event Hubs**:
- Need > 100k events/sec sustained
- Log/telemetry ingestion (fire-and-forget acceptable)
- Integration with Azure Stream Analytics required

---

## ADR-004: Cosmos DB Consistency Model

**Status**: Accepted

**Context**:
Cosmos DB offers 5 consistency levels with different latency/availability trade-offs.

**Decision**: Session Consistency (default)

**Rationale**:
1. **Read-your-writes** - Users see their own events immediately
2. **Predictable latency** - Bounded staleness overhead avoided
3. **Multi-region ready** - Works with geo-replication
4. **Balance** - Not as expensive as Strong, more consistent than Eventual

**Query-specific overrides**:
- Analytics queries: Use `ConsistencyLevel.Eventual` for cost
- User-facing reads: Session (default)

---

## ADR-005: Hot/Cold Data Strategy

**Status**: Accepted

**Context**:
Event data has different access patterns over time.

**Decision**: 
- **Hot (0-30 days)**: Cosmos DB with TTL
- **Cold (30+ days)**: Future ADX/Blob integration (Phase 2)

**Current Implementation**:
- TTL: 30 days (2,592,000 seconds) on CosmosEventDocument
- Container default TTL enabled
- No current cold storage tier

**Future Enhancement**:
- Change Feed → Azure Functions → Blob Storage (Parquet)
- Query with Synapse Serverless or ADX

---

## ADR-006: Rate Limiting Algorithm Choice

**Status**: Accepted

**Context**:
Need to protect system from traffic spikes and abuse.

**Algorithms Considered**:
| Algorithm | Pros | Cons |
|-----------|------|------|
| Fixed Window | Simple | Boundary burst problem |
| Sliding Window | Smooth limits | More memory |
| Token Bucket | Bursty-friendly | Complexity |
| Leaky Bucket | Smooth output | Not bursty-friendly |

**Decision**: Dual-algorithm approach
- **Token Bucket** (per-IP): 100 tokens, refill 10/sec
- **Sliding Window** (global): 10k requests/minute

**Rationale**:
1. Token Bucket allows legitimate bursts (e.g., batch uploads)
2. Sliding Window prevents global overload
3. Dual-layer prevents single point of failure

**Implementation**: `RateLimitingMiddleware.cs` using `System.Threading.RateLimiting`
---

## ADR-007: Resilient Governance Patch (v2.0)

**Status**: Accepted (Principal Grade)

**Context**: 
Addressing architectural gaps regarding trust boundaries, idempotency terminology, and heuristic maturity.

**Decisions**:

### 1. Trusted Proxy Enforcement
- **Problem**: Blindly trusting `X-Forwarded-For` allows IP spoofing.
- **Solution**: Strictly extract client IP from the rightmost non-trusted entry in the XFF chain.
- **Enforcement**: Only requests from known Edge Proxies (e.g. Front Door, Local Gateway) are processed for location-based risk.

### 2. Idempotent Processing Effects (Refined v2.1)
- **Stability Improvement**: Moved away from hashing mutable fields (Timestamp/Payload).
- **Mechanism**: Strictly use the source-generated `EventId` (UUID) as the `DeduplicationId`. If missing, the system generates a hash based on *immutable business keys* only. This ensures that client-side retries with different timestamps are correctly deduplicated.

### 3. Confidence-Weighted Risk (Refined v2.1)
- **Problem**: Logarithmic curves starting at 0 create a "Blank Check" vulnerability for the first transaction.
- **Solution**: Implemented a **Sigmoid-Floor** (`0.5 + (n/10 * 0.5)`).
- **Impact**: Even the first transaction has a 50% confidence factor.
- **Bypass Logic**: Critical signals (e.g. Impossible Travel) bypass the confidence multiplier entirely to ensure immediate protection against high-certainty attacks.

### 5. Materialized View & Rehydration (Refined v2.1)
- **Strategy**: Established a **"Start-From-Beginning" Rehydration Policy**.
- **Implementation**: If a Read-View (Component) is corrupted or lags significantly, a new container is provisioned, and the Change Feed processor is restarted with `StartFromBeginning = true` to rebuild the state. 
- **Observability**: Added "Lag Metrics" (Staleness Offset) to the dashboard to inform operators of the data's freshness.

**Consequences**:
- ✅ Immune to basic XFF spoofing.
- ✅ Mathematical certainty in risk dampening (no magic numbers).
- ✅ Formal idempotent safety (Principal-grade).
- ⚠️ Adds SHA-256 overhead to ingestion (insignificant vs network IO).
