---
description: Running verification tests for the CloudScale Platform
---

# Validation Workflows

This document outlines how to run the validation scripts to verify different aspects of the CloudScale Event Intelligence Platform.

## Prerequisites
- Activate the Python virtual environment:
  ```bash
  source .venv/bin/activate
  ```
- Ensure all Docker containers are running:
  ```bash
  docker ps
  ```

## 1. Fraud Detection Test
Simulates a velocity-based attack (excessive requests from a single IP) to trigger the fraud detection logic.

**Command:**
```bash
python test_fraud.py
```

**Expected Output:**
- Requests sent: 25 (all accepted 202)
- Waiting period...
- Active Alerts Found: > 0 (e.g., 7-15)
- Alert details with "Suspicious Activity Detected".

## 2. Load Testing
Tests the system's throughput capabilities.

**Command:**
```bash
python load_test_optimized.py
```

**Configuration (in file):**
- `TARGET_RPS`: Target Requests Per Second (default: 10000)
- `DURATION_SEC`: Test duration (default: 10)

## 3. Data Integrity Test
Verifies that events sent to the API are successfully persisted in Cosmos DB.

**Command:**
```bash
python verify_data_integrity.py
```

**Expected Output:**
- Baseline count retrieved.
- Batch of 100 events sent.
- Final count matches Baseline + 100.
