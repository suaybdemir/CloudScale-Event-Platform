using System.Collections.Concurrent;
using CloudScale.Shared.Events;
using Azure.Messaging.ServiceBus.Administration;

namespace CloudScale.IngestionApi.Services;

public interface IStatsService
{
    void RecordEvent(EventBase @event);
    void RecordSuccess();
    void RecordFailure();
    (long Total, long Fraud) GetCounts();
    IEnumerable<dynamic> GetRecentAlerts();
    IEnumerable<EventBase> GetRecentEvents();
    IEnumerable<dynamic> GetTopUsers();
    object GetDetailedStats();
}

public class InMemoryStatsService : IStatsService
{
    private long _totalEvents = 0;
    private long _fraudCount = 0;
    private long _successCount = 0;
    private long _failureCount = 0;
    
    private readonly ConcurrentQueue<dynamic> _recentAlerts = new();
    private readonly ConcurrentQueue<EventBase> _recentEvents = new(); // For Audit Log
    private readonly ConcurrentDictionary<string, int> _userScores = new();
    private readonly ConcurrentDictionary<string, int> _eventTypeCounts = new();
    
    private readonly ServiceBusAdministrationClient? _adminClient;
    private readonly string _queueName;
    private readonly int _maxConcurrent;
    
    private double _avgLatencyMs = 0;
    private long _processedCount = 0;
    private readonly DateTime _startTime = DateTime.UtcNow;

    public InMemoryStatsService(IConfiguration config)
    {
        _queueName = config["ServiceBus:QueueName"] ?? "events-ingestion";
        var connectionString = config["ServiceBus:ConnectionString"];
        
        // Read real platform limit or default
        _maxConcurrent = config.GetValue<int>("Kestrel:Limits:MaxConcurrentConnections", 10000);

        if (!string.IsNullOrEmpty(connectionString) && !connectionString.Contains("mock"))
        {
            try {
                _adminClient = new ServiceBusAdministrationClient(connectionString);
            } catch { /* Ignore if fails during init */ }
        }
    }

    private int _eventsInCurrentSecond = 0;
    private long _lastSecond = 0;
    private double _currentEps = 0;

    public void RecordEvent(EventBase @event)
    {
        Interlocked.Increment(ref _totalEvents);

        // Real-time EPS calculation (1s precision)
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Interlocked.Read(ref _lastSecond) != nowSeconds)
        {
            var oldSecond = Interlocked.Exchange(ref _lastSecond, nowSeconds);
            if (oldSecond != 0)
            {
                var events = Interlocked.Exchange(ref _eventsInCurrentSecond, 1);
                // Simple EMA for smoothness
                _currentEps = (_currentEps * 0.4) + (events * 0.6);
            }
            else
            {
                Interlocked.Increment(ref _eventsInCurrentSecond);
            }
        }
        else
        {
            Interlocked.Increment(ref _eventsInCurrentSecond);
        }

        // Track Event Types
        _eventTypeCounts.AddOrUpdate(@event.EventType ?? "unknown", 1, (key, old) => old + 1);

        // Track Latency
        var latency = (DateTime.UtcNow - @event.CreatedAt).TotalMilliseconds;
        if (latency > 0)
        {
             UpdateLatency(latency);
        }

        bool isSuspicious = false;
        if (@event.Metadata != null && @event.Metadata.TryGetValue("IsSuspicious", out var suspObj))
        {
            if (suspObj is bool b) isSuspicious = b;
            else if (suspObj?.ToString()?.ToLower() == "true") isSuspicious = true;
        }

        if (isSuspicious && @event.Metadata != null)
        {
            Interlocked.Increment(ref _fraudCount);
            
            var riskScore = 0;
            if (@event.Metadata.TryGetValue("RiskScore", out var rsObj))
            {
                if (rsObj is int rs) riskScore = rs;
                else if (rsObj != null && int.TryParse(rsObj.ToString(), out var parsedRs)) riskScore = parsedRs;
            }

            var riskLevel = riskScore >= 80 ? "Critical" : 
                            riskScore >= 60 ? "High" : 
                            riskScore >= 40 ? "Medium" : "Low";

            var alert = new 
            {
                id = @event.EventId,
                eventType = @event.EventType,
                userId = @event.UserId,
                createdAt = @event.CreatedAt.ToString("o"),
                clientIp = @event.Metadata.ContainsKey("ClientIp") ? @event.Metadata["ClientIp"]?.ToString() : null,
                riskScore = riskScore,
                riskLevel = riskLevel,
                riskReason = @event.Metadata.ContainsKey("RiskReason") ? @event.Metadata["RiskReason"]?.ToString() : "Suspicious Activity"
            };
            
            _recentAlerts.Enqueue(alert);
            while (_recentAlerts.Count > 20) _recentAlerts.TryDequeue(out _);
        }

        if (!string.IsNullOrEmpty(@event.UserId))
        {
            int score = @event.EventType switch { "page_view" => 1, "user_action" => 10, "purchase" => 50, _ => 0 };
            if (score > 0)
            {
                _userScores.AddOrUpdate(@event.UserId, score, (key, old) => old + score);
            }
        }
        
        // Track recent events for audit log
        _recentEvents.Enqueue(@event);
        while (_recentEvents.Count > 50) _recentEvents.TryDequeue(out _);
    }

    public void RecordSuccess() => Interlocked.Increment(ref _successCount);
    public void RecordFailure() => Interlocked.Increment(ref _failureCount);

    private void UpdateLatency(double newLatency)
    {
        var count = Interlocked.Increment(ref _processedCount);
        if (count == 1) _avgLatencyMs = newLatency;
        else
        {
            _avgLatencyMs = (_avgLatencyMs * 0.9) + (newLatency * 0.1);
        }
    }

    public (long Total, long Fraud) GetCounts() => (_totalEvents, _fraudCount);

    public IEnumerable<dynamic> GetRecentAlerts() => _recentAlerts.Reverse().Take(10);
    public IEnumerable<EventBase> GetRecentEvents() => _recentEvents.Reverse().Take(20);

    public IEnumerable<dynamic> GetTopUsers() => _userScores.OrderByDescending(kv => kv.Value).Take(10).Select(kv => new { id = kv.Key, totalScore = kv.Value, eventCount = kv.Value });

    private long _cachedQueueDepth = 0;
    private DateTime _lastQueueDepthUpdate = DateTime.MinValue;

    public object GetDetailedStats()
    {
        // Decay logic: If no events for more than 2 seconds, drop throughput to 0
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (nowSeconds - Interlocked.Read(ref _lastSecond) > 2)
        {
            _currentEps = 0;
            Interlocked.Exchange(ref _eventsInCurrentSecond, 0);
        }

        // Update Queue Depth in background to avoid blocking the API thread
        if (_adminClient != null && (DateTime.UtcNow - _lastQueueDepthUpdate).TotalSeconds > 10)
        {
            _lastQueueDepthUpdate = DateTime.UtcNow;
            _ = Task.Run(async () => {
                try {
                    var props = await _adminClient.GetQueueRuntimePropertiesAsync(_queueName);
                    _cachedQueueDepth = props.Value.ActiveMessageCount;
                } catch { /* Silent fail */ }
            });
        }

        double successRate = 100.0;
        long totalRequests = _successCount + _failureCount;
        if (totalRequests > 0)
        {
            successRate = Math.Round((double)_successCount / totalRequests * 100, 2);
        }

        return new 
        {
            stats = new { 
                totalEvents = _totalEvents, 
                fraudCount = _fraudCount, 
                queueDepth = _cachedQueueDepth, 
                successRate, 
                throughput = Math.Round(_currentEps, 1),
                targetThroughput = 2000 // 100% Baseline
            },
            distribution = _eventTypeCounts,
            performance = new { avgLatencyMs = Math.Round(_avgLatencyMs, 2), uptimeSeconds = (DateTime.UtcNow - _startTime).TotalSeconds },
            system = new {
                apiStatus = "Healthy",
                processorStatus = "Healthy",
                apiReplicas = 1,
                processorReplicas = 3,
                maxConcurrent = (int)_maxConcurrent
            }
        };
    }
}
