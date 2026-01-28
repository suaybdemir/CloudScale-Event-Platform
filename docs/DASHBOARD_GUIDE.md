# Dashboard Operator Guide

**Access URL**: `http://localhost:3001`

The Operations Dashboard provides real-time visibility into the system's "Nervous System". It is designed for **SREs** and **Operators**, not Business Analysts.

---

## 1. Metric Sources & Lag

The dashboard aggregates data from two distinct consistency domains. Understanding this is critical for debugging.

| Widget | Source | Latency/Lag | Purpose |
| :--- | :--- | :--- | :--- |
| **Queue Depth** | Service Bus (Direct) | Real-time | **Immediate Health**. If > 0, system is backing up. |
| **Throughput** | Redis (Atomic Counters) | < 1s | **Traffic Check**. "Are we receiving data?" |
| **Risk Leaderboard** | Redis (Sorted Sets) | < 1s | **Security**. "Who is attacking us?" |
| **Verification Table** | Cosmos DB (Query) | ~2-5s | **Consistency**. "Did data actually land on disk?" |

> **Operational Note**: It is normal for "Verification Table" to trail behind "Throughput" by a few seconds. If this lag grows > 1 minute, the **Processing Layer** is likely stuck or crashed.

---

## 2. Key Indicators

### A. "The Pulse" (Events/Sec)
*   **Normal**: 0 - 4,000.
*   **Warning**: > 4,000 (Approaching Emulator Limit).
*   **Critical**: 0 (System Down) or Flatline at 4,112 (Saturation).

### B. Queue Depth
*   **Healthy**: 0-100 (Transient bursts).
*   **Degraded**: 100-1,000 (Processing slower than Ingestion).
*   **Critical**: > 1,000 (Consumers stalled / Backpressure failing).
    *   *Action*: Check `docker logs event-processor`.

### C. Live Security Alerts
Displays "Bypassed" risks or high-confidence fraud detections.
*   **Red Bar**: Confidence > 80%. Immediate blocking usually active.

---

## 3. Database Verification Tool

The "Blue Button" (`Database Verification`) triggers a manual consistency check.
1.  It queries the last 50 claimed "Accepted" events from the API logs.
2.  It actively queries Cosmos DB for these IDs.
3.  **Green Status**: Event found on disk.
4.  **Red Status**: Event missing (Data Loss Risk).
