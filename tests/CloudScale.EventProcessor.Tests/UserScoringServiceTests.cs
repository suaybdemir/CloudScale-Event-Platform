using CloudScale.EventProcessor.Services;
using CloudScale.Shared.Events;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace CloudScale.EventProcessor.Tests;

public class UserScoringServiceTests
{
    private readonly Mock<Container> _containerMock;
    private readonly Mock<CosmosClient> _cosmosClientMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly UserScoringService _service;

    public UserScoringServiceTests()
    {
        _containerMock = new Mock<Container>();
        _cosmosClientMock = new Mock<CosmosClient>();
        _configMock = new Mock<IConfiguration>();
        var loggerMock = new Mock<ILogger<UserScoringService>>();

        _configMock.Setup(c => c["CosmosDb:DatabaseName"]).Returns("EventsDb");
        
        _cosmosClientMock.Setup(c => c.GetContainer("EventsDb", "UserProfiles"))
            .Returns(_containerMock.Object);

        _service = new UserScoringService(_cosmosClientMock.Object, _configMock.Object, loggerMock.Object);
    }

    [Fact]
    public async Task UpdateUserScoreAsync_ShouldIncrementScore_ForPageView()
    {
        // Arrange
        var @event = new PageViewEvent 
        { 
            Url = "https://example.com", 
            UserId = "user1", 
            CorrelationId = "123", 
            TenantId = "tenant1" 
        };

        // Act
        await _service.UpdateUserScoreAsync(@event, CancellationToken.None);

        // Assert
        // Verify PatchItemAsync called with score 1
        _containerMock.Verify(c => c.PatchItemAsync<dynamic>(
            "user1",
            It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("tenant1"))),
            It.Is<IReadOnlyList<PatchOperation>>(ops => 
                ops.Any(op => op.OperationType == PatchOperationType.Increment && op.Path == "/totalScore" && GetPatchValue(op) == 1) 
            ),
            It.IsAny<PatchItemRequestOptions>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task UpdateUserScoreAsync_ShouldUseValues_ForPurchase()
    {
        // Arrange
        var @event = new PurchaseEvent
        { 
            ActionName = "checkout", 
            UserId = "user2", 
            CorrelationId = "123", 
            TenantId = "tenant1",
            Amount = 99.99m
        };

        // Act
        await _service.UpdateUserScoreAsync(@event, CancellationToken.None);

        // Assert
        // Verify PatchItemAsync called with score 50 (Purchase)
        _containerMock.Verify(c => c.PatchItemAsync<dynamic>(
            "user2",
            It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey("tenant1"))),
            It.Is<IReadOnlyList<PatchOperation>>(ops => 
                ops.Any(op => op.OperationType == PatchOperationType.Increment && op.Path == "/totalScore" && GetPatchValue(op) == 50)
            ),
            It.IsAny<PatchItemRequestOptions>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }
    
    // Helper to get value from internal/protected property or generic
    private long GetPatchValue(PatchOperation op)
    {
        // Try to find a "Value" property via reflection
        var prop = op.GetType().GetProperty("Value", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (prop != null)
        {
            var val = prop.GetValue(op);
            if (val is int i) return i;
            if (val is long l) return l;
            return 0; // Unknown numeric
        }
        return 0;
    }
}
