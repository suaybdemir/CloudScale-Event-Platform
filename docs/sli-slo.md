# Service Level Objectives (SLO) & Error Budgets

This document defines the reliability targets and the operational policies triggered when those targets are missed.

---

## 1. Definitions

*   **SLI (Indicator)**: Doing the math. (e.g., `(Good Req / Total Req) * 100`).
*   **SLO (Objective)**: The target. (e.g., `99.9%`).
*   **Error Budget**: The allowed failure room. (e.g., `0.1%` or `43 minutes` per month).

---

## 2. Defined SLOs

### A. Ingestion Availability
*   **Definition**: Percentage of requests returning `202 Accepted` vs `5xx`.
*   **Target**: **99.9%** (Monthly).
*   **Budget**: ~43 minutes of unavailability allowed per month.
*   **Note**: `429 Too Many Requests` (Rate Limit) are **Excluded** from the error count (they are "Expected Behavior" for abusive clients).

### B. Ingestion Latency
*   **Definition**: Time from Request Header received to Response Header sent.
*   **Target**: **p99 < 200ms**.
*   **Rationale**: Mobile clients will timeout if > 2s. We need a safety margin.

### C. End-to-End Freshness (Lag)
*   **Definition**: Time from `Event.Timestamp` to `CosmosDB.InsertTimestamp`.
*   **Target**: **p99 < 5 seconds**.
*   **Rationale**: Dashboard needs "Real-Time" feel.

---

## 3. Operational Policies

### Policy: "Budget Burn"
What happens when we burn through our error budget?

1.  **Burn Rate > 10% / hour**:
    *   **Action**: Page On-Call.
    *   **Response**: Check "Zombie Containers" or "Port Conflicts".

2.  **Budget Exhausted (0% remaining)**:
    *   **Policy**: **HALT ALL FEATURE DEPLOYMENTS**.
    *   **Focus**: Sprint scope shifts 100% to **Reliability & Technical Debt** until budget resets (next month).
    *   **Override**: Only VP of Engineering can authorize a deployment during Budget Exhaustion (Emergency Security Fixes only).

---

## 4. Alerting Thresholds (Prometheus/AppInsights)

| Alert Name | Query Logic | Duration | Severity |
| :--- | :--- | :--- | :--- |
| `HighErrorRate` | `FailureRate > 1%` | 5m | **P1 (Page)** |
| `NearSaturation` | `Throughput > 3,800 RPS` | 5m | **P2 (Warn)** |
| `ConsumerLag` | `QueueDepth > 2,000` | 10m | **P2 (Warn)** |
| `DeadLetter` | `DLQ_Count > 0` | Immediate | **P3 (Ticket)** |
