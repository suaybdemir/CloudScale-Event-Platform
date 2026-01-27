using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CloudScale.IngestionApi.Telemetry;

/// <summary>
/// Custom metrics for CloudScale Event Intelligence Platform.
/// Exposed via OpenTelemetry for Application Insights / Prometheus.
/// </summary>
public static class EventMetrics
{
    public static readonly string MeterName = "CloudScale.Events";
    
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    
    // Counters
    private static readonly Counter<long> EventsIngested = 
        Meter.CreateCounter<long>("cloudscale_events_ingested_total", "events", 
            "Total number of events ingested");
    
    private static readonly Counter<long> EventsProcessed = 
        Meter.CreateCounter<long>("cloudscale_events_processed_total", "events", 
            "Total number of events processed");
    
    private static readonly Counter<long> FraudDetected = 
        Meter.CreateCounter<long>("cloudscale_fraud_detected_total", "events", 
            "Total number of fraud events detected");
    
    private static readonly Counter<long> RateLimitRejections = 
        Meter.CreateCounter<long>("cloudscale_rate_limit_rejections_total", "requests", 
            "Total number of requests rejected by rate limiter");
    
    private static readonly Counter<long> ServiceBusErrors = 
        Meter.CreateCounter<long>("cloudscale_servicebus_errors_total", "errors", 
            "Total number of Service Bus errors");
    
    // Histograms
    private static readonly Histogram<double> IngestionDuration = 
        Meter.CreateHistogram<double>("cloudscale_ingestion_duration_seconds", "seconds", 
            "Duration of event ingestion");
    
    private static readonly Histogram<double> ProcessingDuration = 
        Meter.CreateHistogram<double>("cloudscale_processing_duration_seconds", "seconds", 
            "Duration of event processing");
    
    // Gauges (using ObservableGauge)
    private static long _queueDepth = 0;
    private static readonly ObservableGauge<long> QueueDepth = 
        Meter.CreateObservableGauge("cloudscale_queue_depth", () => _queueDepth, "messages", 
            "Current Service Bus queue depth");

    // Activity Source for distributed tracing
    public static readonly ActivitySource ActivitySource = new("CloudScale.IngestionApi", "1.0.0");

    // Public methods to record metrics
    public static void RecordEventIngested(string eventType, string tenantId)
    {
        EventsIngested.Add(1, 
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("tenant_id", tenantId));
    }

    public static void RecordEventProcessed(string eventType, bool isSuspicious)
    {
        EventsProcessed.Add(1, 
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("is_suspicious", isSuspicious));
    }

    public static void RecordFraudDetected(string eventType, string tenantId)
    {
        FraudDetected.Add(1, 
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("tenant_id", tenantId));
    }

    public static void RecordRateLimitRejection(string clientIp)
    {
        RateLimitRejections.Add(1, 
            new KeyValuePair<string, object?>("client_ip_hash", clientIp.GetHashCode())); // Hash for privacy
    }

    public static void RecordServiceBusError(string errorType)
    {
        ServiceBusErrors.Add(1, 
            new KeyValuePair<string, object?>("error_type", errorType));
    }

    public static void RecordIngestionDuration(double seconds, string eventType)
    {
        IngestionDuration.Record(seconds, 
            new KeyValuePair<string, object?>("event_type", eventType));
    }

    public static void RecordProcessingDuration(double seconds, string eventType)
    {
        ProcessingDuration.Record(seconds, 
            new KeyValuePair<string, object?>("event_type", eventType));
    }

    public static void SetQueueDepth(long depth)
    {
        _queueDepth = depth;
    }
}
