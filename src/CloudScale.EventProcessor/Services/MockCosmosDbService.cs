using CloudScale.Shared.Events;

namespace CloudScale.EventProcessor.Services;

public class MockCosmosDbService : ICosmosDbService
{
    private readonly ILogger<MockCosmosDbService> _logger;

    public MockCosmosDbService(ILogger<MockCosmosDbService> logger)
    {
        _logger = logger;
    }

    public Task AddEventAsync(EventBase @event, CancellationToken ct)
    {
        _logger.LogInformation("[MOCK] Saved event {EventId} to Cosmos DB (Simulated)", @event.EventId);
        return Task.CompletedTask;
    }

    public Task<bool> HasPurchaseAsync(string userId, DateTimeOffset since, CancellationToken ct)
    {
        // Simulate no purchases for safety or random?
        return Task.FromResult(false);
    }

    public Task UpdateSystemHealthAsync(bool isUnderPressure, int recommendedConcurrency, CancellationToken ct)
    {
        _logger.LogDebug("[MOCK] System Health updated: Pressure={Pressure}", isUnderPressure);
        return Task.CompletedTask;
    }
}
