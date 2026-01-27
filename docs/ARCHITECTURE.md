# CloudScale Event Intelligence Platform - System Architecture

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
    API -.->|4. Throttling Status 429| Throttling
    
    %% Legend
    linkStyle 10,11,12,13 stroke:#D32F2F,stroke-width:3px;
```

## Mimari Bileşenler ve Akış

1.  **Edge Layer:** Kullanıcı trafiği **Azure Front Door** üzerinden gelir, **WAF** ile temizlenir.
2.  **Ingestion Layer:** İstekler **Load Balancer** ile dağıtılır. Eğer sistem "baskı altındaysa", **Throttling Middleware** istekleri reddeder (429).
3.  **Messaging Layer:** Kabul edilen olaylar **Service Bus Standard**'a fırlatılır. Hatalı mesajlar **DLQ**'ya düşer.
4.  **Processing Layer:** **Event Processor** mesajları işler, **Fraud Detection** yapar.
5.  **Monitor & Adjust (Geri Bildirim Döngüsü):** 
    *   `BackpressureMonitor` kuyruğu izler. Limit aşılırsa Cosmos DB'ye "Alarm" yazar.
    *   API bu alarmı görür ve Throttling'i aktif eder.
6.  **Storage & Analytics:** İşlenen veriler **Cosmos DB**'ye (Hot) ve **Blob Storage**'a (Cold) yazılır. **Synapse** soğuk veriyi analiz eder.
