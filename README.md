# ☁️ CloudScale Event Intelligence Platform

**Production-grade, high-throughput event ingestion and real-time analytics system designed for 10k+ events/min with sub-200ms latency.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Azure](https://img.shields.io/badge/Azure-Native-0078D4?logo=microsoftazure)](https://azure.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![CI/CD](https://img.shields.io/badge/CI%2FCD-GitHub_Actions-2088FF?logo=githubactions)](/.github/workflows/ci-cd.yml)

---

## 🎯 Mission

Enable enterprises to ingest, process, and analyze millions of user behavior events in real-time with **99.9% availability**, **fraud detection**, and **actionable insights** — all on Azure-native infrastructure.

---

## 🏗️ Architecture Overview

The system implements a robust **Hot/Cold Storage** pattern with a **Self-Healing Feedback Loop**.

```mermaid
graph TD
    %% Styling
    classDef azure fill:#0072C6,stroke:#fff,stroke-width:2px,color:#fff;
    classDef code fill:#5D4037,stroke:#fff,stroke-width:2px,color:#fff;
    classDef storage fill:#388E3C,stroke:#fff,stroke-width:2px,color:#fff;
    classDef analytics fill:#F57C00,stroke:#fff,stroke-width:2px,color:#fff;
    classDef monitor fill:#D32F2F,stroke:#fff,stroke-width:2px,color:#fff;

    subgraph ClientLayer [Clients]
        MobileApp(Mobile App)
        WebApp(Web Dashboard)
    end

    subgraph EdgeLayer [Edge Layer]
        AFD[Azure Front Door]:::azure
        WAF[Web App Firewall]:::azure
        AFD --> WAF
    end

    subgraph IngestionLayer [Ingestion Layer]
        LB[Load Balancer / Nginx]:::code
        API[Ingestion API Cluster]:::code
        Throttling[Throttling Middleware]:::monitor
        
        LB --> Throttling
        Throttling --> API
    end

    subgraph MessagingLayer [Messaging Layer]
        SB[Azure Service Bus - Standard]:::azure
        Topic[Events Topic]:::azure
        DLQ[Dead Letter Queue]:::azure
        
        SB --> Topic
        Topic -.-> DLQ
    end

    subgraph ProcessingLayer [Processing Layer]
        Worker[Event Processor]:::code
        Fraud[Fraud Detection Engine]:::code
        Health[Backpressure Monitor]:::monitor
        
        Worker --> Fraud
        Worker <--> Health
    end

    subgraph StorageLayer [Data Persistence]
        Cosmos[Azure Cosmos DB - Hot]:::storage
        Blob[Azure Blob Storage - Archive]:::storage
    end

    subgraph AnalyticsLayer [Analytics Layer]
        Synapse[Azure Synapse Analytics]:::analytics
        PBI[Power BI Dashboard]:::analytics
    end

    %% Flows
    MobileApp --> AFD
    WebApp --> AFD
    WAF --> LB
    API -->|High Throughput| SB
    
    Topic -->|Subscription| Worker
    
    Worker -->|Risk Analysis| Cosmos
    Worker -->|Archiving| Blob
    Blob -->|Batch Ingest| Synapse
    Synapse --> PBI
    
    %% Feedback Loop (Monitor & Adjust)
    Health -.->|1. Monitor Queue Depth| SB
    Health -.->|2. Update Health State| Cosmos
    Cosmos -.->|3. Read Pressure State| API
    API -.->|4. Throttling (429)| Throttling
    
    %% Legend
    linkStyle 10,11,12,13 stroke:#D32F2F,stroke-width:3px;
```

### Component Responsibilities

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Edge Layer** | Azure Front Door + WAF | Global load balancing, DDoS protection, edge security. |
| **Ingestion API** | .NET 10 Minimal API | High-performance event intake with **Smart Throttling**. |
| **Message Broker** | Azure Service Bus (Standard) | Reliable async event delivery with Topics & DLQ. |
| **Event Processor** | .NET Worker Service | Fraud detection, enrichment, **Hot/Cold Storage** routing. |
| **Hot Storage** | Azure Cosmos DB | Low-latency event storage (30-day TTL). |
| **Cold Storage** | Azure Blob Storage (Archive) | Long-term archival for compliance and big data analysis. |
| **Analytics** | Azure Synapse Analytics | Serverless SQL queries on archived data. |
| **Feedback Loop** | System Health Watcher | **Autoscaling & Self-Healing** (Monitor & Adjust). |

---

## 💡 Key Capabilities

### 1. Smart Throttling (Monitor & Adjust)
The system is self-aware. If the queue builds up pressure:
*   **Monitor:** `EventProcessor` detects high queue depth (>1000).
*   **Signal:** Updates `SystemHealth` state in Cosmos DB.
*   **Adjust:** `IngestionAPI` reads this state and actively rejects new requests with **429 Too Many Requests** until pressure subsides.

### 2. Hot & Cold Storage Strategy
*   **Hot Path:** Critical events stored in Cosmos DB for instant query (Dashboard).
*   **Cold Path:** ALL events archived to Azure Blob Storage (cheaper, long-term) for Synapse analytics.

### 3. Fraud Detection
Real-time analysis of user behavior velocity.
*   Rule: >10 requests/minute from same IP → **Flag as Suspicious**.
*   Process: Marked events are stored safely but trigger security alerts.

---

## 🛠️ Technical Stack

| Layer | Technology | Why This Choice |
|-------|------------|-----------------|
| **Framework** | .NET 10 (Preview) | Cutting edge performance, AOT readiness. |
| **Messaging** | Azure Service Bus | Enterprise-grade reliability (FIFO, Sessions). |
| **Database** | Azure Cosmos DB | Single-digit ms latency, global scale. |
| **Storage** | Azure Blob Storage | Cost-effective tiering (Hot/Cool/Archive). |
| **Analytics** | Azure Synapse | Unification of Big Data and Data Warehousing. |
| **IaC** | Bicep | Declarative Azure infrastructure as code. |
| **CI/CD** | GitHub Actions | Automated build, test, and deployment pipelines. |

---

## 🚀 Getting Started

### Prerequisites
*   Docker & Docker Compose
*   .NET 10 SDK (or .NET 8)
*   Azure CLI (optional for cloud deployment)
*   git

### Local Development (Docker)

1.  **Clone the repository:**
    ```bash
    git clone https://github.com/suaybdemir/CloudScale-Event-Platform.git
    cd CloudScale-Event-Platform
    ```

2.  **Start the environment:**
    ```bash
    docker compose up -d
    ```
    *This starts the API, Worker, Nginx, Cosmos DB Emulator, Azurite (Blob), Service Bus Emulator and SQL Edge.*

3.  **Verify Health:**
    Visit endpoint: `http://localhost:5000/health`

4.  **View Data:**
    *   **Dashboard:** [http://localhost:3001](http://localhost:3001)
    *   **Cosmos Explorer:** [https://localhost:8081/_explorer/index.html](https://localhost:8081/_explorer/index.html)

### Azure Deployment (IaC)

Deploy the entire infrastructure to Azure using Bicep:

```bash
az login
az deployment group create \
  --resource-group rg-cloudscale-prod \
  --template-file infra/main.bicep \
  --parameters environmentName=prod
```

---

## 🤝 Contributing

1.  Fork the repository
2.  Create feature branch (`git checkout -b feature/amazing-feature`)
3.  Commit with conventional commits (`feat:`, `fix:`, `docs:`)
4.  Push and create Pull Request

---

## 📄 License

This project is licensed under the MIT License - see [LICENSE](LICENSE) file.

---

<div align="center">
  <b>Built with ❤️ for high-scale, production-grade systems</b>
</div>
