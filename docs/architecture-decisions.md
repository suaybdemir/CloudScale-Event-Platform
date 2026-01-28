# Architecture Decision Records (ADR)

This document captures the **Principal-level architectural decisions** for the CloudScale Event Intelligence Platform. Each record describes the context, decision, alternatives considered, and the strictly accepted trade-offs.

---

## ADR-001: Synchronous Ingestion Model

**Status**: Accepted (Reverted from Async Batching)

**Context**: 
High-throughput ingestion systems often choose between **Synchronous (RPC-style)** and **Asynchronous (Fire-and-Forget)** patterns.
*   *Attempt 1 (Async)*: We implemented a `Channel<T>` based buffered producer to maximize throughput.
*   *Result*: System instability. Backpressure handling in the in-memory buffer proved complex to tune for the single-node emulator environment, leading to OOM crashes and zombie containers.

**Decision**: 
Use **Synchronous Ingestion** (Client waits for Service Bus `Ack`).

**Evaluation of Trade-offs**:
| Feature | Synchronous (Chosen) | Asynchronous (Rejected) |
| :--- | :--- | :--- |
| **Stability** | **High**. Backpressure is natural (TCP connection slows down). | **Low**. Requires complex memory management (Drop strategies). |
| **Throughput** | Limited by Network Round Trip (~4k RPS). | Potentially higher (10k+ RPS). |
| **Data Safety** | **High**. Client knows if data is persisted. | **Medium**. Data in memory buffer is lost on crash. |
| **Complexity** | **Low**. Standard SDK usage. | **High**. Custom threading, graceful shutdown logic. |

**Accepted Risk**:
By choosing Sync, we accept a **hard throughput ceiling (~4,000 RPS)** on the current hardware. This is acceptable because:
1.  It is sufficient for current validation goals.
2.  It prevents the "Silent Failure" mode where the API accepts requests but drops them silently during crash-loops.

---

## ADR-002: Docker Compose for Orchestration

**Status**: Accepted (with constraints)

**Context**:
Choosing an orchestration platform for the Reference Implementation.

**Decision**: 
Use **Docker Compose** on a single Vertical Scaled Host (i7-2600).

**Why not Kubernetes (AKS/K3s)?**
*   **Operational Overhead**: Maintaing a K8s control plane on a single legacy host (i7-2600) consumes 20-30% of available resources.
*   **Debuggability**: `docker logs` and `volume` mapping is functionally superior for the current "Inner Loop" development phase.

**Explicit Limitations (The "Price" of Simplicity)**:
1.  **No Network Isolation**: All containers share the host bridge network. A compromised container can probe others.
2.  **Zero-Downtime Deployment**: Not supported. `docker compose up -d` causes brief downtime during container swap.
3.  **Manual Scaling**: No HPA (Horizontal Pod Autoscaler). Scaling requires manual `docker compose up --scale`.

---

## ADR-003: Clickstream Data Consistency

**Status**: Accepted

**Context**:
Clickstream/Analytics data has high volume but low individual value per event.

**Decision**: 
Prioritize **Availability** and **Throughput** over **Strict Consistency** (AP over CP).

**ACCEPTED DATA LOSS RISK**:
*   **Scenario**: In the rare event of a sudden host power failure or OOM crash.
*   **Impact**: In-flight requests (approx 50-100ms window) may be lost.
*   **Justification**: For aggregate analytics (e.g., "Trending Topics"), losing <0.1% of events is statistically insignificant. Blocking 100% of traffic to ensure 0% loss (e.g., Two-Phase Commit) is considered an **Anti-Pattern** for this specific workload.

---

## ADR-004: Emulator-First Development

**Status**: Accepted

**Context**:
Developing against real Cloud resources introduces cost and latency.

**Decision**: 
Use exact-match Emulators (Service Bus, Cosmos DB, Sql Edge).

**Constraint**: 
Emulator performance/behavior does **NOT** map 1:1 to Production.
*   **Partitioning**: Emulators often ignore partition keys for physical storage.
*   **Latency**: Local loopback (<1ms) masks real cloud network latency (10-50ms).
*   **Conclusion**: Stress tests on Emulators validate **Application Logic Stability**, NOT **Production Capacity**.

---

## ADR-005: Idempotency Strategy

**Status**: Accepted

**Context**:
Network retries can cause duplicate event processing.

**Decision**: 
Client-side `Idempotency-Key` (or deterministic hashing of `Source` + `ID`).

**Responsibility**:
*   **Client**: MUST retry on 5xx/Timeout.
*   **Server**: MUST dedup based on ID.
*   **Limitations**: Deduplication window is limited by Cache TTL (processed event IDs are stored in Redis for 10 minutes). Duplicates arriving after 10m may be re-processed. This is an **accepted eventual consistency tradeoff**.
