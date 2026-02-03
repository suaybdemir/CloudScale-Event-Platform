using Azure.Messaging.ServiceBus.Administration;

namespace CloudScale.EventProcessor.Services;

/// <summary>
/// Monitors Service Bus queue depth and signals backpressure conditions.
/// Emits metrics for alerting and adaptive processing.
/// </summary>
public interface IBackpressureMonitor
{
    bool IsUnderPressure { get; }
    long CurrentQueueDepth { get; }
    int RecommendedConcurrency { get; }
}

/// <summary>
/// Decision: D001 — Backpressure Strategy (Part of Service Bus over Kafka decision)
/// This component monitors queue depth and propagates backpressure to API layer via Cosmos.
/// 
/// KNOWN ISSUE: Backpressure signal writes to Cosmos (UpdateSystemHealthAsync).
/// If Cosmos is under RU pressure (F001), the health write may fail or delay.
/// Control-plane isolation is DEFERRED — see docs/decision-to-code.md#d001
/// </summary>
public class BackpressureMonitor : BackgroundService, IBackpressureMonitor
{
    private readonly ICosmosDbService _cosmosService;
    private readonly ILogger<BackpressureMonitor> _logger;
    private readonly string? _connectionString;
    private readonly string _queueName;
    private readonly ServiceBusAdministrationClient? _adminClient;
    
    // Decision: D001 — Hardcoded thresholds (runtime change requires redeploy)
    // These values are empirical, not load-tested beyond 5K events/sec
    private const long LowPressureThreshold = 1000;    // Normal operation
    private const long MediumPressureThreshold = 5000;  // Start reducing concurrency
    private const long HighPressureThreshold = 10000;   // Critical - minimum concurrency
    
    // Concurrency levels
    private const int MaxConcurrency = 32;
    private const int MediumConcurrency = 16;
    private const int MinConcurrency = 4;
    
    private long _currentDepth = 0;
    private bool _isUnderPressure = false;
    private int _recommendedConcurrency = MaxConcurrency;

    public bool IsUnderPressure => _isUnderPressure;
    public long CurrentQueueDepth => _currentDepth;
    public int RecommendedConcurrency => _recommendedConcurrency;

    public BackpressureMonitor(IConfiguration config, ICosmosDbService cosmosService, ILogger<BackpressureMonitor> logger)
    {
        _logger = logger;
        _cosmosService = cosmosService;
        _connectionString = config["ServiceBus:ConnectionString"];
        _queueName = config["ServiceBus:QueueName"] ?? "events-ingestion";
        
        if (!string.IsNullOrEmpty(_connectionString) && !_connectionString.Contains("mock"))
        {
            _adminClient = new ServiceBusAdministrationClient(_connectionString);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_adminClient == null)
        {
            _logger.LogWarning("BackpressureMonitor disabled: No valid Service Bus connection");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        _logger.LogInformation("BackpressureMonitor started. Monitoring queue: {QueueName}", _queueName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var props = await _adminClient.GetQueueRuntimePropertiesAsync(_queueName, stoppingToken);
                _currentDepth = props.Value.ActiveMessageCount + props.Value.ScheduledMessageCount;
                
                UpdatePressureState();
                
                _logger.LogDebug("Queue depth: {Depth}, UnderPressure: {Pressure}, Concurrency: {Concurrency}",
                    _currentDepth, _isUnderPressure, _recommendedConcurrency);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get queue properties");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private void UpdatePressureState()
    {
        var previousPressure = _isUnderPressure;
        var previousConcurrency = _recommendedConcurrency;
        
        if (_currentDepth >= HighPressureThreshold)
        {
            _isUnderPressure = true;
            _recommendedConcurrency = MinConcurrency;
        }
        else if (_currentDepth >= MediumPressureThreshold)
        {
            _isUnderPressure = true;
            _recommendedConcurrency = MediumConcurrency;
        }
        else if (_currentDepth < LowPressureThreshold)
        {
            _isUnderPressure = false;
            _recommendedConcurrency = MaxConcurrency;
        }
        
        // Log state changes
        if (previousPressure != _isUnderPressure || previousConcurrency != _recommendedConcurrency)
        {
            if (_isUnderPressure)
            {
                _logger.LogWarning("BACKPRESSURE: Queue depth {Depth} exceeded threshold. Reducing concurrency to {Concurrency}",
                    _currentDepth, _recommendedConcurrency);
            }
            else
            {
                _logger.LogInformation("Backpressure relieved. Queue depth: {Depth}. Restoring concurrency to {Concurrency}",
                    _currentDepth, _recommendedConcurrency);
            }

            // Sync with Cosmos for API Throttling (Monitor & Adjust Loop)
            _ = _cosmosService.UpdateSystemHealthAsync(_isUnderPressure, _recommendedConcurrency, CancellationToken.None);
        }
    }
}
