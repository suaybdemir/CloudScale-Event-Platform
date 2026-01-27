using Microsoft.Azure.Cosmos;
using CloudScale.Shared.Events;
using CloudScale.EventProcessor.Configuration;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System.Text.Json.Serialization;

namespace CloudScale.EventProcessor.Services;

public interface ICosmosDbService
{
    Task AddEventAsync(EventBase @event, CancellationToken ct);
    Task<bool> HasPurchaseAsync(string userId, DateTimeOffset since, CancellationToken ct);
    Task UpdateSystemHealthAsync(bool isUnderPressure, int recommendedConcurrency, CancellationToken ct);
}

public class CosmosDbService : ICosmosDbService
{
    private readonly Container _container;
    private readonly ILogger<CosmosDbService> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public CosmosDbService(CosmosClient cosmosClient, IOptions<CosmosDbSettings> settings, ILogger<CosmosDbService> logger)
    {
        _logger = logger;
        var config = settings.Value;
        _container = cosmosClient.GetContainer(config.DatabaseName, config.ContainerName);
        
        // Polly retry pipeline for transient Cosmos errors
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder()
                    .Handle<CosmosException>(ex => 
                        ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                        ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                        ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout),
                DelayGenerator = args =>
                {
                    // Use Cosmos-provided RetryAfter if available
                    if (args.Outcome.Exception is CosmosException cosmosEx && cosmosEx.RetryAfter.HasValue)
                    {
                        return ValueTask.FromResult<TimeSpan?>(cosmosEx.RetryAfter.Value);
                    }
                    return ValueTask.FromResult<TimeSpan?>(null); // Use default
                },
                OnRetry = args =>
                {
                    _logger.LogWarning("Cosmos retry {Attempt}/{Max} after {Delay}ms. Exception: {Message}",
                        args.AttemptNumber, 5, args.RetryDelay.TotalMilliseconds, 
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task AddEventAsync(EventBase @event, CancellationToken ct)
    {
        var document = new CosmosEventDocument(@event);
        
        await _resiliencePipeline.ExecuteAsync(async token =>
        {
            try
            {
                await _container.CreateItemAsync<dynamic>(document, new PartitionKey(document.PartitionKey), cancellationToken: token);
                _logger.LogInformation("Saved event {EventId} to Cosmos DB", @event.EventId);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Principal Safeguard: 2-step Idempotency Validation
                // 1. Fetch the existing record to compare payloads
                var existing = await _container.ReadItemAsync<CosmosEventDocument>(document.id, new PartitionKey(document.PartitionKey), cancellationToken: token);
                
                if (existing.Resource.EventData.PayloadHash != @event.PayloadHash)
                {
                    _logger.LogCritical("IDEMPOTENCY COLLISION: Event {EventId} received with DIFFERENT payload! Potential Replay/Tampering Attack.", @event.EventId);
                    // In a production system, we might throw a specific exception here to be handled by the worker (e.g., move to DLQ)
                }
                else
                {
                    _logger.LogWarning("Duplicate event {EventId} with identical payload (ignored)", @event.EventId);
                }
            }
        }, ct);
    }

    public async Task<bool> HasPurchaseAsync(string userId, DateTimeOffset since, CancellationToken ct)
    {
        var query = new QueryDefinition(
            "SELECT VALUE COUNT(1) FROM c WHERE c.EventData.EventType = 'purchase' AND c.EventData.UserId = @userId AND c.EventData.CreatedAt >= @since")
            .WithParameter("@userId", userId)
            .WithParameter("@since", since);

        using var iterator = _container.GetItemQueryIterator<int>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            return response.FirstOrDefault() > 0;
        }
        return false;
    }

    public async Task UpdateSystemHealthAsync(bool isUnderPressure, int recommendedConcurrency, CancellationToken ct)
    {
        var healthDoc = new
        {
            id = "system:health",
            PartitionKey = "system",
            IsUnderPressure = isUnderPressure,
            RecommendedConcurrency = recommendedConcurrency,
            LastUpdated = DateTimeOffset.UtcNow
        };

        try
        {
            await _container.UpsertItemAsync(healthDoc, new PartitionKey("system"), cancellationToken: ct);
            _logger.LogDebug("System Health updated in Cosmos DB: UnderPressure={Pressure}", isUnderPressure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update System Health in Cosmos DB");
        }
    }
}

public record CosmosEventDocument
{
    public string id { get; init; }
    public string PartitionKey { get; init; }
    public EventBase EventData { get; init; }
    
    /// <summary>
    /// TTL in seconds. Default 30 days for hot data.
    /// Set to -1 to disable TTL for this document.
    /// </summary>
    [JsonPropertyName("ttl")]
    public int TimeToLive { get; init; } = 2592000; // 30 days

    public CosmosEventDocument(EventBase @event)
    {
        // Use DeduplicationId (Principal-grade) for formal idempotency
        // Fallback to EventId for legacy/manual events
        id = @event.DeduplicationId ?? @event.EventId; 
        EventData = @event;
        // Synthetic PK: TenantId:yyyy-MM (Principal-grade Hybrid Partitioning)
        PartitionKey = $"{@event.TenantId}:{@event.CreatedAt:yyyy-MM}";
    }
}
