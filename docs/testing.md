# Performance Verification & Testing Strategy

This document details the **Verification Methodology**, **Benchmark Results**, and **Stress Testing Protocols** for the CloudScale Platform.

> **Current Benchmark (v1.2 - Synchronous Mode):**
> *   **Max Sustained Throughput**: ~4,112 RPS
> *   **Hardware**: Intel Core i7-2600 (Single Node)
> *   **Bottleneck**: Service Bus Emulator Disk I/O

---

## 1. Official Benchmark Results

### 1.1 Load Test: Saturation (10k RPS)
**Scenario**: Attempting to push 10,000 req/sec (2.5x capacity) to verify failure mode.

| Metric | Result | Analysis |
| :--- | :--- | :--- |
| **Target RPS** | 10,000 | Simulated burst traffic. |
| **Actual RPS** | **4,112** | Hard limit reached. |
| **Success Rate** | ~41% | System shed 59% of load. |
| **Failure Mode** | `ClientConnectorError` / `Timeout` | **Correct Behavior**. The API stopped accepting connections to protect internal resources (Load Shedding). |
| **Recovery** | **Instant** | As soon as load stopped, API availability returned to 100%. |

### 1.2 Bottleneck Analysis
Why 4,112 RPS?

1.  **Service Bus Emulator**:
    *   The Emulator writes messages to a local SQL Express (Linux) instance inside the container.
    *   At ~4k RPS, the **Dish I/O** on the host machine becomes saturated by the SQL write-ahead log (WAL).
    *   This increases `SendAsync` latency from <10ms to >500ms.
2.  **Synchronous Backpressure**:
    *   Since ingestion is Synchronous (See [ADR-001](architecture-decisions.md)), increased Service Bus latency directly slows down HTTP request processing.
    *   Kestrel thread pool fills up, and new connections are dropped.

> **Principal Insight**: To scale beyond 4k RPS, we must move from **Emulator** to **Real Azure Service Bus** (which handles 10k+ easily) or upgrade Host I/O (NVMe SSD).

---

## 2. Test Suites

### 2.1 Unit Tests (`src/tests`)
*   **Scope**: Domain Logic, Validators, Risk Engine Math.
*   **Command**: `dotnet test`
*   **Coverage**: Target > 80% (Business Logic).

### 2.2 Integration Tests
*   **Scope**: API -> Service Bus -> Cosmos DB flow.
*   **Command**: `dotnet test --filter Category=Integration`
*   **Prerequisite**: Requires `docker compose up` stack running.

### 2.3 Stress Tests (Python)
Located in `deploy_env/bin/`:

1.  `run_stress_test.py`:
    *   Standard verification test.
    *   Ramps up to 10k RPS for 30 seconds.
    *   Used to validate "Acceptance" criteria.

2.  `load_test_10k.py`:
    *   The internal script executed by the above.
    *   Uses `aiohttp` for non-blocking high-concurrency generation.

---

## 3. How to Reproduce

### Prerequisite
Ensure the stack is running and healthy (Check Dashboard at `http://localhost:3001`).

### Step 1: Run the diagnostic
Check if the system is ready to accept load.
```bash
python deploy_env/bin/python diagnose_remote.py
```

### Step 2: Execute Stress Test
```bash
python deploy_env/bin/python run_stress_test.py
```

### Step 3: Interpret Results
*   **Pass**: "Actual RPS" is between 3500-4500. "Failed" requests are acceptable if they are Timeouts (Load Shedding).
*   **Fail**: "Actual RPS" < 1000 or significant "Connection Refused" errors (Indicating Service Crash).

---

## 4. Failure Scenarios

| Scenario | Expected Behavior | Observation |
| :--- | :--- | :--- |
| **Service Bus Down** | API returns 503 immediately. | Circuit Breaker Open. |
| **Cosmos DB Down** | Ingestion OK, Processing stops. | Queue Depth grows. |
| **Redis Down** | API returns 200 (Risk bypassed). | Fail-Open security policy. |
