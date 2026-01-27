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
        
        // Build Polly resilience pipeline: Retry + Circuit Breaker
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
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 10,
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
