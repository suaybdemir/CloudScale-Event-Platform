using Azure.Messaging.ServiceBus;
using CloudScale.Shared.Events;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Text.Json;
using CloudScale.Shared.Constants;

namespace CloudScale.IngestionApi.Services;

public interface IServiceBusProducer
{
    Task PublishAsync(EventBase @event, CancellationToken cancellationToken = default);
}

public class ServiceBusProducerService : IServiceBusProducer, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusProducerService> _logger;
    private readonly IStatsService _stats;
    
    // Polly Resilience Pipeline
    private readonly ResiliencePipeline _resiliencePipeline;

    public ServiceBusProducerService(ServiceBusClient client, IConfiguration config, ILogger<ServiceBusProducerService> logger, IStatsService stats)
    {
        _client = client;
        _logger = logger;
        _stats = stats;
        var queueName = config["ServiceBus:QueueName"];
        if(string.IsNullOrEmpty(queueName)) throw new ArgumentNullException("ServiceBus:QueueName config is missing");
        
        _sender = _client.CreateSender(queueName);
        
        // Decision: D001 — Azure Service Bus over Apache Kafka
        // Decision: D005 — Circuit Breaker Strategy (Service Bus side: PROTECTED)
        // This pipeline has both Retry AND Circuit Breaker.
        // Compare with CosmosDbService.cs which has Retry ONLY (D005 gap).
        // See: docs/decision-to-code.md#d001, #d005
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<ServiceBusException>(ex => ex.IsTransient),
                OnRetry = args =>
                {
                    _logger.LogWarning("Retry {AttemptNumber} for Service Bus send after {Delay}ms", 
                        args.AttemptNumber, args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            // Decision: D005 — Default Circuit Breaker Thresholds
            // Failure Scenario: F003 — False positive risk due to small sample size
            // WARNING: 10 calls minimum + 50% failure = 5 failures to trip.
            // GC pause or transient blip can trigger false open. Tuning DEFERRED.
            // See: docs/failure-scenarios.md#f003
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,                    // 50% failures trigger open
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 10,                // Small sample — F003 risk
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder().Handle<ServiceBusException>(),
                OnOpened = args =>
                {
                    _logger.LogError("Circuit breaker OPENED for Service Bus. Duration: {Duration}s", 
                        args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Circuit breaker CLOSED for Service Bus");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("Circuit breaker HALF-OPEN for Service Bus");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task PublishAsync(EventBase @event, CancellationToken cancellationToken = default)
    {
        _stats.RecordEvent(@event); // Optimistic Update

        var json = JsonSerializer.Serialize(@event, @event.GetType());
        var message = new ServiceBusMessage(json)
        {
            CorrelationId = @event.CorrelationId,
            Subject = @event.EventType,
            PartitionKey = @event.TenantId,
            ApplicationProperties = 
            {
                { "EventType", @event.EventType },
                { "SchemaVersion", @event.SchemaVersion }
            }
        };

        if (@event.Metadata != null)
        {
            try 
            {
                foreach (var kvp in @event.Metadata)
                {
                   if (kvp.Key != null && kvp.Value != null)
                   {
                        // Ensure value is a primitive or string, as JsonElement is not supported by Service Bus
                        message.ApplicationProperties[kvp.Key] = kvp.Value.ToString();
                   }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FAILED TO MAP METADATA for event {EventId}", @event.EventId);
            }
        }

        // Decision: D001 — Service Bus vendor lock-in point
        // Failure Scenario: F002 — Emulator ceiling (~4K msg/sec) breaks here
        // When SendAsync latency spikes, this is where thread pool backs up.
        // See: docs/failure-scenarios.md#f002
        await _resiliencePipeline.ExecuteAsync(async token =>
        {
            await _sender.SendMessageAsync(message, token);
            _logger.LogInformation("Published event {EventId} of type {EventType}", @event.EventId, @event.EventType);
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
