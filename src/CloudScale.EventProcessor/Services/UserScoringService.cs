using CloudScale.Shared.Events;
using Microsoft.Azure.Cosmos;

namespace CloudScale.EventProcessor.Services;

public interface IUserScoringService
{
    Task UpdateUserScoreAsync(EventBase @event, CancellationToken ct);
}

public class UserScoringService : IUserScoringService
{
    private readonly Container _container;
    private readonly ILogger<UserScoringService> _logger;

    public UserScoringService(CosmosClient cosmosClient, IConfiguration config, ILogger<UserScoringService> logger)
    {
        _logger = logger;
        _container = cosmosClient.GetContainer(config["CosmosDb:DatabaseName"], "UserProfiles");
    }

    public async Task UpdateUserScoreAsync(EventBase @event, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(@event.UserId)) return;

        int score = @event.EventType switch
        {
            "page_view" => 1,
            "user_action" => 10,
            "purchase" => 50,
            _ => 0
        };

        if (score == 0) return;

        // Optimistic Concurrency or Patch API
        // Patch allows us to increment without read-modify-write
        try
        {
            await _container.PatchItemAsync<dynamic>(
                id: @event.UserId,
                partitionKey: new PartitionKey(@event.TenantId), // Assuming Tenant partitioning for Users
                patchOperations: new[]
                {
                    PatchOperation.Increment("/totalScore", score),
                    PatchOperation.Set("/lastActive", DateTime.UtcNow),
                    PatchOperation.Increment("/eventCount", 1)
                },
                cancellationToken: ct
            );
            
            _logger.LogDebug("User {UserId} score enriched by {Score}", @event.UserId, score);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Create profile if not exists
            var profile = new
            {
                id = @event.UserId,
                tenantId = @event.TenantId,
                totalScore = score,
                eventCount = 1,
                lastActive = DateTime.UtcNow
            };
            
            await _container.CreateItemAsync(profile, new PartitionKey(@event.TenantId), cancellationToken: ct);
             _logger.LogInformation("Created profile for User {UserId}", @event.UserId);
        }
    }
}
