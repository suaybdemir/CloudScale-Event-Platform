# CloudScale Event Platform — Decision Log

This document captures key architectural decisions, rejected alternatives, and explicit trade-offs. Each entry follows the format:

- **Decision**: What we chose
- **Context**: Why it mattered at the time (including organizational constraints)
- **Alternatives Considered**: What we didn't choose and why
- **Consequences**: What we accepted by making this choice
- **Impacts**: Which components/docs are affected
- **Revisit Trigger**: When and who should reconsider

---

## Organizational Context

**Team Size:** Solo developer / small team (1-2 engineers)  
**Ops Capacity:** No dedicated on-call rotation. Incidents handled best-effort during business hours.  
**Environment:** Development/reference platform. No production SLA.  
**Skill Focus:** Azure-native stack preferred for career alignment.

These constraints heavily influence every decision below.

---

## D001: Azure Service Bus over Apache Kafka

**Decision:** Use Azure Service Bus Emulator for event messaging.

**Context:** Given a solo developer with no 24/7 ops capacity, operational simplicity trumps raw throughput. Kafka requires Zookeeper/KRaft management, partition tuning, and consumer group coordination — too much overhead for a reference platform.

**Alternatives Considered:**

| Option | Why Rejected |
|--------|--------------|
| Apache Kafka | Operational complexity too high for team size. Would require dedicated ops time we don't have. |
| RabbitMQ | Viable, but Azure-native skills transfer preferred for production readiness. |
| Redis Streams | Lacks dead-letter queue semantics and durable replay guarantees. |

**Consequences:**

- ✅ Simpler ops, faster iteration
- ❌ Vendor lock-in to Azure messaging semantics
- ❌ Lower throughput ceiling (~4K msg/sec on emulator vs Kafka's 100K+)
- ❌ Skills not transferable to Kafka-based organizations

**Impacts:** Ingestion API (sync publish), Event Processor (message consumption), architecture.md §2.2

**Revisit Trigger:** If throughput exceeds 10K events/sec or multi-region is needed. **Owner:** Lead engineer reviews quarterly throughput metrics.

---

## D002: CQRS with Eventual Consistency

**Decision:** Separate read and write paths. Accept read lag for write throughput.

**Context:** Early prototypes showed read queries contending with writes. p99 latency spiked during ingestion bursts. With no dedicated DB team, simpler isolation beats complex tuning.

**Alternatives Considered:**

| Option | Why Rejected |
|--------|--------------|
| Strong consistency (single model) | Write throughput degraded under read load. Contention unacceptable. |
| Event sourcing with full replay | Complexity too high for current team size. Replay mechanics not needed yet. |

**Consequences:**

- ✅ Write path isolated from read load
- ✅ Read models optimized for specific query patterns
- ❌ Temporary inconsistency between write and read
- ❌ Increased system complexity (two data paths)

**Impacts:** Storage layer (Cosmos DB), Read models, Dashboard queries, architecture.md §1

**Revisit Trigger:** If consistency gap exceeds 5 seconds or users report stale data confusion. **Owner:** Monitor `read_lag_seconds` metric in Grafana.

---

## D003: Time-Based Partition Key in Cosmos DB

**Decision:** Partition by event timestamp (hourly buckets).

**Context:** Most queries target recent events (last 1-24 hours). With limited RU budget, optimizing for the common case beats generalized access.

**Alternatives Considered:**

| Option | Why Rejected |
|--------|--------------|
| Partition by tenant ID | Not applicable — single-tenant scope. |
| Partition by event type | Would scatter time-range queries across partitions. |
| Synthetic partition key | Added complexity without clear benefit at current scale. |

**Consequences:**

- ✅ Fast queries for recent data
- ❌ Hot partitions during high-traffic hours (09:00-11:00)
- ❌ RU consumption spikes during bursts
- ❌ Cold partitions waste provisioned throughput

**Impacts:** Cosmos DB storage, Dashboard latency, RU cost, architecture.md §2.1

**Revisit Trigger:** If RU throttling exceeds 5% of requests. **Owner:** Weekly RU consumption review, alert on `cosmos_throttled_requests > threshold`.

---

## D004: Idempotency at Consumer Level (Not Broker)

**Decision:** Broker provides at-least-once delivery. Consumers handle idempotency.

**Context:** Exactly-once requires distributed transactions or broker support. Both add latency and complexity beyond current team capacity to maintain.

**Alternatives Considered:**

| Option | Why Rejected |
|--------|--------------|
| Exactly-once via distributed transactions | Latency penalty unacceptable. Coordination overhead high. |
| Broker-level deduplication | Service Bus doesn't support natively. Custom layer = more code to maintain. |

**Consequences:**

- ✅ Simpler broker configuration
- ✅ Consumer idempotency is explicit and testable
- ❌ Duplicate processing possible during failures
- ❌ Consumers must track processed event IDs

**Impacts:** Event Processor (dedup logic), Consumer retry behavior, failure-scenarios.md

**Revisit Trigger:** If duplicate events cause data corruption or user-visible issues. **Owner:** Monitor `duplicate_events_processed` counter.

---

## D005: Default Circuit Breaker Thresholds

**Decision:** Use Polly defaults. No custom tuning.

**Context:** Tuning breakers requires production traffic patterns. We don't have them. Guessing thresholds is worse than using tested defaults.

**Alternatives Considered:**

| Option | Why Rejected |
|--------|--------------|
| Aggressive (fail fast) | Risk of false positives during normal variance. |
| Conservative (fail slow) | Risk of cascading failures during real outages. |
| Adaptive thresholds | Requires ML/heuristics we haven't built. |

**Consequences:**

- ✅ Fast initial deployment
- ❌ Breakers may trip incorrectly
- ❌ No data on threshold effectiveness

**Impacts:** Event Processor resilience, Cosmos DB consumer, failure-scenarios.md (cascading failure risk)

**Revisit Trigger:** After first production incident or load test beyond 5K events/sec. **Owner:** Post-incident review must include breaker behavior analysis.

---

## D006: No Multi-Region Support

**Decision:** Single-region only. Geo-redundancy out of scope.

**Context:** Multi-region requires replication infra, conflict resolution, latency management. Not justified for a solo-developer reference platform with no uptime SLA.

**Alternatives Considered:**

| Option | Why Rejected |
|--------|--------------|
| Active-passive failover | Still requires replication we can't maintain. |
| Active-active | Conflict resolution not designed. Fundamental redesign needed. |

**Consequences:**

- ✅ Simpler architecture
- ✅ No cross-region latency
- ❌ Single point of failure
- ❌ No disaster recovery

**Impacts:** Entire system availability, architecture.md §Scope Warning

**Revisit Trigger:** If SLA requirement exceeds 99.9% or production deployment planned. **Owner:** Product decision, not engineering.

---

## D007: Observability over Raw Throughput

**Decision:** Instrument everything. Accept overhead.

**Context:** With no dedicated ops team, debugging without observability is impossible. The solo developer must be able to understand failures after the fact.

**Alternatives Considered:**

| Option | Why Rejected |
|--------|--------------|
| Minimal logging | Debugging incidents near-impossible. |
| Sampling (1%) | Loses visibility into tail latency and rare failures. |

**Consequences:**

- ✅ Full tracing for every event
- ✅ Metrics for every component
- ❌ ~5-10% throughput overhead
- ❌ Higher log storage costs

**Impacts:** All components, architecture.md §3, Grafana dashboards

**Revisit Trigger:** If observability overhead becomes throughput bottleneck. **Owner:** Monitor `observability_overhead_percent` metric.

---

## Decisions Not Made (Explicit Deferral)

| Topic | Status | Reason | Owner |
|-------|--------|--------|-------|
| Chaos engineering | Deferred | No production traffic to validate against | Revisit after first load test |
| Auto-scaling | Deferred | Single-node scope | Revisit if K8s deployment planned |
| Multi-tenancy | Deferred | Adds isolation complexity | Revisit if multi-customer use case emerges |
| Schema evolution | Deferred | No breaking changes anticipated | Revisit before V2 API |

---

## Cross-Reference Index

| Decision | Related Docs | Affected Components |
|----------|--------------|---------------------|
| D001 | architecture.md §2 | Ingestion API, Event Processor |
| D002 | architecture.md §1 | Storage, Read Models, Dashboard |
| D003 | architecture.md §2.1 | Cosmos DB, RU budget |
| D004 | failure-scenarios.md | Event Processor, Consumer logic |
| D005 | failure-scenarios.md | All resilience patterns |
| D006 | architecture.md §Scope | Entire system |
| D007 | architecture.md §3 | All components, monitoring |

---

## How to Read This Log

- **Evaluating the system:** Start with D001-D003 for core trade-offs.
- **Debugging:** Check D004 (idempotency) and D005 (circuit breakers) first.
- **Extending:** Check "Decisions Not Made" before adding features.
- **Post-incident:** Cross-reference failure scenarios with decision impacts.
