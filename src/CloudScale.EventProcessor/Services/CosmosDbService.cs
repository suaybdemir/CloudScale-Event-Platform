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
        
        // Decision: D005 — Circuit Breaker Strategy
        // WARNING: This pipeline has RETRY ONLY, NO CIRCUIT BREAKER.
        // Failure Scenario: F001 — RU exhaustion retries accelerate cascade instead of failing fast.
        // Production deployment MUST add .AddCircuitBreaker() after .AddRetry().
        // See: docs/decision-to-code.md#d005
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
            // Decision: D004 — Idempotency at Consumer Level
            // Failure Scenario: F004 — Idempotency Key Collision
            // This catch block is the ONLY protection against silent data loss from ID collisions.
            // LogCritical below is the first signal — it MUST trigger alerts in production.
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // 2-step Idempotency Validation: fetch existing, compare PayloadHash
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

    // Decision: D003a — Time-Based Partition Key Selection
    // Decision: D003b — Hot Partition Mitigation (EXPLICITLY UNIMPLEMENTED)
    // Failure Scenario: F001 — All monthly traffic hits same partition, causing RU exhaustion.
    // Known gap: No per-partition monitoring, no write shedding, no suffix randomization.
    // See: docs/decision-to-code.md#d003b
    public CosmosEventDocument(EventBase @event)
    {
        // D004: Idempotency key — collision here causes F004
        id = @event.DeduplicationId ?? @event.EventId; 
        EventData = @event;
        // D003a: Partition key formula — hot partition risk lives here
        PartitionKey = $"{@event.TenantId}:{@event.CreatedAt:yyyy-MM}";
    }
}
