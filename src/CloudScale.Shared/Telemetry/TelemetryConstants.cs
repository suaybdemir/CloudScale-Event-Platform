namespace CloudScale.Shared.Telemetry;

public static class TelemetryConstants
{
    public const string ServiceName = "CloudScale";
    public const string IngestionApiSource = "CloudScale.IngestionApi";
    public const string EventProcessorSource = "CloudScale.EventProcessor";
    
    public static class Metrics
    {
        public const string EventsIngested = "events_ingested_total";
        public const string EventsProcessed = "events_processed_total";
        public const string ProcessingLatency = "processing_latency_seconds";
        public const string QueueDepth = "service_bus_queue_depth";
    }
}
