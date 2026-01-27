using CloudScale.IngestionApi.Services;
using CloudScale.Shared.Events;

namespace CloudScale.IngestionApi.Services.Mocks;

public class MockServiceBusProducer : IServiceBusProducer
{
    private readonly ILogger<MockServiceBusProducer> _logger;
    private readonly Microsoft.Azure.Cosmos.Container? _container;
    private readonly IStatsService _stats;

    public MockServiceBusProducer(
        ILogger<MockServiceBusProducer> logger, 
        Microsoft.Azure.Cosmos.CosmosClient cosmosClient,
        IConfiguration config,
        IStatsService stats)
    {
        _logger = logger;
        _stats = stats;
        if (cosmosClient != null)
        {
            var dbName = config["CosmosDb:DatabaseName"] ?? "EventsDb";
            var containerName = config["CosmosDb:ContainerName"] ?? "Events";
            _container = cosmosClient.GetContainer(dbName, containerName);
        }
    }

    public async Task PublishAsync(EventBase @event, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[ENTRY] PublishAsync called for event {@event.EventId}, type {@event.EventType}, userId {@event.UserId}");
        _logger.LogInformation("[MOCK] Published event {EventId} of type {EventType} to Service Bus (Simulated)", 
            @event.EventId, @event.EventType);

        _stats.RecordEvent(@event); // Live Update

        if (_container != null)
        {
            // Fire-and-forget to simulate Queue decoupling and speed up Ingestion
            _ = Task.Run(async () => 
            {
                try
                {
                    // 1. Simulate Fraud Detection
                    var random = new Random();
                    var fraudRoll = random.Next(100);
                    
                    if (@event.EventType == "page_view" && fraudRoll < 20) 
                    {
                        if (!@event.Metadata.ContainsKey("IsSuspicious"))
                        {
                            @event.Metadata.Add("IsSuspicious", true);
                            if (fraudRoll < 5) { @event.Metadata.Add("RiskLevel", "High"); @event.Metadata.Add("RiskReason", "Velocity Limit Exceeded"); }
                            else if (fraudRoll < 12) { @event.Metadata.Add("RiskLevel", "Medium"); @event.Metadata.Add("RiskReason", "Suspicious Access Pattern"); }
                            else { @event.Metadata.Add("RiskLevel", "Low"); @event.Metadata.Add("RiskReason", "Unusual Behavior Detected"); }
                        }
                    }

                    // 2. Persist Event
                    var doc = new 
                    {
                        id = @event.EventId,
                        PartitionKey = $"{@event.TenantId}:{@event.CreatedAt:yyyy-MM}",
                        EventData = new
                        {
                            @event.EventId,
                            @event.CorrelationId,
                            @event.TenantId,
                            @event.UserId,
                            @event.EventType,
                            @event.CreatedAt,
                            @event.SchemaVersion,
                            @event.Metadata
                        },
                        ttl = -1
                    };
                    await _container.CreateItemAsync(doc, new Microsoft.Azure.Cosmos.PartitionKey(doc.PartitionKey));
                    
                    // 3. User Scoring
                    if (!string.IsNullOrEmpty(@event.UserId))
                    {
                        var usersContainer = _container.Database.GetContainer("UserProfiles");
                        int score = @event.EventType switch { "page_view" => 1, "user_action" => 10, "purchase" => 50, _ => 0 };
                        if (score > 0)
                        {
                            await usersContainer.PatchItemAsync<dynamic>(
                                id: @event.UserId,
                                partitionKey: new Microsoft.Azure.Cosmos.PartitionKey(@event.TenantId),
                                patchOperations: new[] { Microsoft.Azure.Cosmos.PatchOperation.Increment("/totalScore", score), Microsoft.Azure.Cosmos.PatchOperation.Set("/lastActive", DateTime.UtcNow), Microsoft.Azure.Cosmos.PatchOperation.Increment("/eventCount", 1) }
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                     _logger.LogWarning("[MOCK] Background persistence failed: {Message}", ex.Message);
                }
            });
        }
    }
}
