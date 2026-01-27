# CloudScale Cyber-HUD Dashboard Guide

![Cyber-HUD Dashboard](images/dashboard_hud.png)

The CloudScale Dashboard is designed as a mission-control interface for high-velocity event systems. It provides real-time visibility into ingestion rates, system health, and data persistence.

## 1. Primary Indicators

### 🚀 Throughput Gauge (The "Speedometer")
The central element of the dashboard. It visualizes the current ingestion rate against the system's baseline capacity.
*   **Normal Operation (Green/Purple):** System is running within comfortable limits (0 - 100% Load).
*   **Overload State (Pulsing Red):** When traffic exceeds the baseline (e.g., >2000 events/sec), the gauge needle pushes into the red zone and the outer ring pulses. This indicates the system is autoscaling or under stress.
*   **Passively Decaying:** When traffic stops, the needle smoothy returns to exactly 0, confirming the "Silence" of the system.

### 📊 Metric Cards
*   **Total Events:** A monotonically increasing counter of all events successfully ingested since startup. Watch this to confirm data flow.
*   **Success Rate:** The ratio of valid to invalid events.
    *   **100% (Green):** System is healthy.
    *   **<99% (Yellow/Red):** Indicates rejected events (bad schema) or 500 errors.

## 2. Verification Modules

### 🕵️ Audit Log (Database Verification)
Located in the bottom or dedicated tab, this is the "Is it real?" check.
*   **Live Stream:** Displays the last 20 events fetched directly from the backend memory/database.
*   **Latency Check:** Compare the timestamp in this log with your current time to verify end-to-end latency.
*   **Risk Scores:** Visible proof that our AI Fraud Detection scanned the event.

### 🚨 Fraud Radar
*   **Risk Score**: A value from 0-100 assigned to every event.
*   **Status Indicators**:
    *   🟢 **Normal (0-40):** Standard user behavior.
    *   🟠 **Suspicious (40-80):** High velocity or impossible travel.
    *   🔴 **Critical (>80):** Confirmed bot or attack signature.

## 3. System Health
*   **Processors:** The number of active `event-processor` replicas (e.g., 3).
*   **Queue Depth:** Should remain near 0. If it climbs, ingestion is outpacing processing.
