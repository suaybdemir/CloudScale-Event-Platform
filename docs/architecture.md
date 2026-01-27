# System Architecture

![System Architecture Diagram](images/system_architecture_diagram.png)

This document provides a comprehensive technical overview of the CloudScale Event Intelligence Platform architecture.

---

## High-Level Architecture

```mermaid
flowchart TB
    subgraph Clients["📱 Client Layer"]
        WEB[Web Apps]
        MOB[Mobile Apps]
        IOT[IoT Devices]
        SRV[Backend Services]
    end
    
    subgraph Edge["🌐 Edge Layer"]
        CDN[Azure CDN / Front Door]
        WAF[Web Application Firewall]
    end
    
    subgraph Ingestion["🔄 Ingestion Layer"]
        LB[Nginx Load Balancer]
        
        subgraph APICluster["API Cluster"]
            API1["Ingestion API\n(Replica 1)"]
            API2["Ingestion API\n(Replica 2)"]
            API3["Ingestion API\n(Replica 3)"]
            API4["Ingestion API\n(Replica 4)"]
        end
    end
    
    subgraph Messaging["📨 Messaging Layer"]
        SB[(Azure Service Bus\nStandard Tier)]
        DLQ[(Dead Letter Queue)]
    end
    
    subgraph Processing["⚙️ Processing Layer"]
        subgraph Workers["Worker Pool"]
            W1["Event Processor\n(Replica 1)"]
            W2["Event Processor\n(Replica 2)"]
        end
        
        FRAUD[Fraud Detection\nService]
        SCORE[User Scoring\nService]
        BP[Backpressure\nMonitor]
    end
    
    subgraph Storage["💾 Storage Layer"]
        COSMOS[(Azure Cosmos DB\nNoSQL API)]
        BLOB[(Azure Blob\nCold Storage)]
    end
    
    subgraph Analytics["📊 Analytics Layer"]
        ADX[(Azure Data Explorer)]
        SYNAPSE[Azure Synapse]
    end
    
    subgraph Observability["📈 Observability"]
        AI[Application Insights]
        LA[Log Analytics]
        ALERT[Azure Monitor Alerts]
    end
    
    Clients --> Edge
    Edge --> LB
    LB --> APICluster
    APICluster --> SB
    SB --> Workers
    SB -.-> DLQ
    Workers --> FRAUD
    Workers --> SCORE
    Workers --> COSMOS
    COSMOS -.->|Change Feed| ADX
    COSMOS -.->|Archive| BLOB
    BP -->|Monitor| SB
    APICluster & Workers --> AI
    AI --> LA
    LA --> ALERT
```

---

## Component Deep-Dive

### 1. Ingestion API

**Technology**: .NET 8 Minimal API

**Responsibilities**:
- Accept HTTP POST requests with event payloads
- Validate event structure (FluentValidation)
- Apply rate limiting (Token Bucket + Sliding Window)
- Enrich events with metadata (timestamp, IP, correlation ID)
- Publish to Service Bus asynchronously

**Key Design Decisions**:
```
┌─────────────────────────────────────────────────────────────┐
│ Request Flow                                                │
├─────────────────────────────────────────────────────────────┤
│ HTTP Request                                                │
│     ↓                                                       │
│ RateLimitingMiddleware (429 if exceeded)                   │
│     ↓                                                       │
│ CorrelationIdMiddleware (inject/forward X-Correlation-Id)  │
│     ↓                                                       │
│ FluentValidation (400 if invalid)                          │
│     ↓                                                       │
│ EventEnrichmentService (add metadata)                       │
│     ↓                                                       │
│ ServiceBusProducer (async publish with Polly retry)        │
│     ↓                                                       │
│ 202 Accepted                                                │
└─────────────────────────────────────────────────────────────┘
```

**Scaling**: 4 replicas behind Nginx, stateless, horizontal scaling via `docker compose --scale`

---

### 2. Rate Limiting

**Algorithm**: Dual-layer protection

```mermaid
flowchart LR
    REQ[Request] --> TB{Token Bucket\nPer-IP}
    TB -->|Has Token| SW{Sliding Window\nGlobal}
    TB -->|No Token| R1[429 + Retry-After]
    SW -->|Under Limit| PASS[Continue]
    SW -->|Over Limit| R2[429 + Retry-After]
```

| Layer | Algorithm | Limit | Purpose |
|-------|-----------|-------|---------|
| Per-IP | Token Bucket | 100 tokens, 10/sec refill | Prevent single-source abuse |
| Global | Sliding Window | 10k/minute | Prevent system overload |

**Why Both?**
- Token Bucket allows legitimate bursts (batch uploads)
- Sliding Window provides global protection
- Defense in depth

---

### 3. Azure Service Bus

**Configuration**:
```
Tier: Standard (Premium for production)
Queue: events-ingestion
Max Size: 5 GB
Message TTL: 14 days
Lock Duration: 5 minutes
Max Delivery Count: 10
```

**Message Flow**:
```mermaid
sequenceDiagram
    participant API as Ingestion API
    participant SB as Service Bus
    participant W as Worker
    participant DLQ as Dead Letter Queue
    participant C as Cosmos DB
    
    API->>SB: Send Message
    SB->>W: Receive Message
    
    alt Success
        W->>C: Write Event
        W->>SB: Complete Message
    else Transient Failure
        W->>SB: Abandon (retry)
    else Poison Message
        W->>DLQ: Dead Letter
    end
```

**Dead Letter Reasons**:
- `MissingEventType` - No event type specified
- `DeserializationFailed` - Invalid JSON
- `UnknownEventType` - Unrecognized event type
- `MaxRetriesExceeded` - Failed after 5 attempts

---

### 4. Event Processor

**Technology**: .NET Worker Service (BackgroundService)

**Processing Pipeline**:
```
Message Received
     ↓
Deserialize by EventType (page_view, purchase, etc.)
     ↓
Fraud Detection (velocity check)
     ↓
User Scoring (behavioral analysis)
     ↓
Cosmos DB Write (with Polly retry)
     ↓
Scheduled Actions (cart abandonment check)
     ↓
Complete Message
```

**Concurrency Control**:
```csharp
MaxConcurrentCalls = 32  // Configurable
PrefetchCount = 100      // Batch optimization
AutoCompleteMessages = false  // Manual ack
```

---

### 5. Cosmos DB

**Configuration**:
```
API: NoSQL (Core)
Consistency: Session (default)
Partition Key: /{TenantId}:{yyyy-MM}
TTL: 30 days (2,592,000 seconds)
RU/s: Autoscale 400-4000
```

**Partition Strategy**:
```mermaid
graph TD
    E[Event] --> PK["Partition Key\nTenantId:yyyy-MM"]
    PK --> P1["acme:2026-01"]
    PK --> P2["acme:2026-02"]
    PK --> P3["contoso:2026-01"]
```

**Why This Strategy?**
1. **Tenant Isolation**: Queries by tenant hit single partition
2. **Time Distribution**: Prevents hot partitions
3. **TTL Alignment**: Old partitions naturally expire
4. **Query Patterns**: Most queries filter by tenant + time range

---

### 6. Backpressure Handling

**Thresholds**:
```mermaid
graph LR
    subgraph Normal["Queue < 1k"]
        N[32 Concurrent]
    end
    subgraph Warning["Queue 5k-10k"]
        W[16 Concurrent]
    end
    subgraph Critical["Queue > 10k"]
        C[4 Concurrent]
    end
    
    Normal -->|Depth Increases| Warning
    Warning -->|Depth Increases| Critical
    Critical -->|Depth Decreases| Warning
    Warning -->|Depth Decreases| Normal
```

**Monitoring**:
- `BackpressureMonitor` checks queue depth every 30 seconds
- Emits `cloudscale_queue_depth` metric
- Alerts at 5k (warning) and 10k (critical)

---

### 7. Observability Stack

```mermaid
graph TB
    subgraph Sources["Data Sources"]
        API[Ingestion API]
        PROC[Event Processor]
    end
    
    subgraph Collection["Collection"]
        OTEL[OpenTelemetry SDK]
    end
    
    subgraph Export["Exporters"]
        AZURE[Azure Monitor]
        OTLP[OTLP Endpoint]
    end
    
    subgraph Visualization["Visualization"]
        AI[App Insights]
        JAEGER[Jaeger/Grafana]
    end
    
    Sources --> OTEL
    OTEL --> AZURE
    OTEL --> OTLP
    AZURE --> AI
    OTLP --> JAEGER
```

**Custom Metrics**:
| Metric | Type | Labels |
|--------|------|--------|
| `cloudscale_events_ingested_total` | Counter | event_type, tenant_id |
| `cloudscale_ingestion_duration_seconds` | Histogram | event_type |
| `cloudscale_fraud_detected_total` | Counter | event_type |
| `cloudscale_queue_depth` | Gauge | - |
| `cloudscale_rate_limit_rejections_total` | Counter | - |

---

## Data Flow Scenarios

### Scenario 1: Normal Event Ingestion

```
1. Client → POST /api/events
2. Nginx → Round-robin to API replica
3. API → Validate → Enrich → Publish to SB (async)
4. API → Return 202 Accepted (< 50ms)
5. Worker → Receive → Process → Write to Cosmos
6. Worker → Complete message
```

### Scenario 2: Traffic Spike (10x Load)

```
1. Rate limiter rejects excess requests (429)
2. Service Bus buffers accepted events (up to 5GB)
3. BackpressureMonitor detects queue growth
4. Concurrency reduced (32 → 16 → 4)
5. Processing slows gracefully
6. Queue drains over time
7. Concurrency restored
```

### Scenario 3: Poison Message

```
1. Worker receives malformed message
2. Deserialization fails
3. Message dead-lettered with reason
4. DLQ alert fires
5. Operator investigates
6. Fix and reprocess or discard
```

---

## Security Considerations

| Layer | Protection |
|-------|------------|
| Network | Azure NSG, Private Endpoints |
| Authentication | Managed Identity (no secrets in code) |
| Secrets | Azure Key Vault |
| Transport | TLS 1.3 |
| API | Rate limiting, input validation |
| Data | Encryption at rest (Cosmos), TTL |

---

## Failure Modes & Mitigations

| Failure | Impact | Mitigation |
|---------|--------|------------|
| API replica down | Reduced capacity | Nginx health checks, auto-restart |
| Service Bus throttle | Message delays | Backpressure monitoring, queue alerts |
| Cosmos DB 429 | Write failures | Polly retry with RetryAfter |
| Worker crash | Message redelivery | Auto-restart, lock renewal |
| Full DLQ | Data loss risk | Alert at >0 messages, capacity monitoring |
