# Architectural Trade-offs & Theory

This document analyzes the theoretical constraints and mathematical realities of the CloudScale architecture.

> **"There are no solutions, there are only trade-offs."** — Thomas Sowell

---

## 1. Throughput vs. Latency (Little's Law)

**Observation**: During the 10k RPS stress test, we hit a hard wall at ~4,112 RPS.
**Theory**: Little's Law ($L = \lambda W$) explains this perfectly.

*   $L$ (Concurrency): The number of concurrent requests Kestrel can handle (Threads/Connections).
*   $\lambda$ (Throughput): Requests Per Second (RPS).
*   $W$ (Wait Time): Latency per request.

**The Math**:
In **Synchronous Mode**, the API holds the connection open until Service Bus acknowledges persistence.
*   **Normal**: Latency ($W$) = 10ms. Max RPS ($\lambda$) = High.
*   **Saturation**: Emulator Disk I/O chokes. Latency ($W$) spikes to 500ms+.
*   **Result**: To maintain $\lambda$ (Throughput) at 4,000 with 500ms latency, we need $L = 4000 \times 0.5 = 2000$ concurrent threads.
*   **Failure**: The thread pool exhausts. Stability ($L$) is finite. Therefore, as $W$ increases, $\lambda$ **MUST** decrease.

**Trade-off Definition**:
*   **We chose**: Resilience (Load Shedding).
*   **We sacrificed**: Queueing (Accepting more than we can chew).

---

## 2. CAP Theorem Application

**Scenario**: Azure Cosmos DB (PaaS) vs. Cosmos Emulator (Local).

### Azure PaaS (Real World)
*   **Configuration**: Multi-Master or Single-Master with Geo-Replication.
*   **Capability**: Can tune consistency (Strong, Bounded Staleness, Session, Eventual).
*   **Trade-off**: You can choose **CP** (Strong Consistency) at the cost of high latency/lower availability during partitions, or **AP** (Eventual) for max throughput.

### Emulator (Our Environment)
*   **Configuration**: Single Container.
*   **Reality**: **CA** (Consistency + Availability) but **NO** Partition Tolerance (P).
*   **Impact**: If the host acts weird (Network bridge issues), the Emulator doesn't "failover". It just hangs ("Zombie State").
*   **Lesson**: Testing distributed algorithms (like consensus) on the Emulator is **invalid**. It simulates the API surface, not the distributed physics.

---

## 3. Queue Depth vs. Backpressure

**Scenario**: The "Buffer" problem.

| Strategy | Behavior | Failure Mode |
| :--- | :--- | :--- |
| **Unbounded Queue** | Accept everything, process later. | **OOM Kill**. Memory grows until process crashes. Data loss = 100% of buffer. |
| **Bounded Queue (Blocking)** | Accept until full, then block caller. | **Cascading Latency**. Upstream callers hang. Thread pool exhaustion. |
| **Bounded Queue (Dropping)** | Accept until full, then drop new (Tail Drop). | **Data Loss**. Events are lost, but system stays alive. |
| **Synchronous (No Queue)** | Caller waits for DB/Broker. | **Backpressure**. Client is forced to slow down (TCP Window). |

**Decision**: We reverted to **Synchronous (No Queue)**.
*   **Why**: Given our Single-Node constraint, any memory buffer competes with the Service Bus Emulator for RAM.
*   **Trade-off**: We force the **Client** to handle the complexity of retries, rather than the **Server** handling the complexity of buffering.
