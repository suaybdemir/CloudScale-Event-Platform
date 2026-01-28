using CloudScale.Shared.Events;
using CloudScale.Shared.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace CloudScale.Shared.Tests;

public class FraudDetectionServiceTests
{
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<FraudDetectionService>> _loggerMock;
    private readonly FraudDetectionService _service;

    public FraudDetectionServiceTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<FraudDetectionService>>();
        _service = new FraudDetectionService(_cacheMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CalculateRiskAsync_ForceSuspicious_ReturnsFixedScore()
    {
        // Arrange
        var @event = new PageViewEvent { TenantId = "t1", CorrelationId = "c1" };
        @event.Metadata["ForceSuspicious"] = "true";

        // Act
        var result = await _service.CalculateRiskAsync(@event);

        // Assert
        Assert.Equal(85, result.RiskScore);
        Assert.Contains("Watchdog", result.Reason);
    }

    [Fact]
    public async Task CalculateRiskAsync_ConfidenceScoring_IncreasesWithCount()
    {
        // Arrange
        var @event = new PageViewEvent { UserId = "user1", TenantId = "t1", CorrelationId = "c1" };
        
        // Mock count = 9 (approaching max confidence)
        byte[] countBytes = System.Text.Encoding.UTF8.GetBytes("9");
        _cacheMock.Setup(x => x.GetAsync("confidence_points_user1", default)).ReturnsAsync(countBytes);

        // Act
        await _service.CalculateRiskAsync(@event);

        // Assert
        // After 10 events (9 cached + 1 current), confidence should be 1.0 (0.5 + 0.5)
        Assert.Equal(1.0, @event.ConfidenceScore);
    }

    [Fact]
    public async Task CalculateRiskAsync_ImpossibleTravel_TriggersRisk()
    {
        // Arrange
        var @event = new PageViewEvent { UserId = "user2", TenantId = "t1", CorrelationId = "c1" };
        @event.Metadata["Location"] = "USA";
        
        // Mock User was in Tokyo 1 minute ago
        _cacheMock.Setup(x => x.GetAsync("fraud_v2_travel_user2", default))
                  .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes("Tokyo"));

        // Act
        var result = await _service.CalculateRiskAsync(@event);

        // Assert
        Assert.True(result.RiskScore >= 60);
        Assert.Contains("Impossible Travel", result.Reason);
    }

    [Fact]
    public async Task CalculateRiskAsync_VelocityCheck_TriggersThresholds()
    {
        // Arrange
        var @event = new PageViewEvent { TenantId = "t1", CorrelationId = "c1" };
        @event.Metadata["ClientIp"] = "1.1.1.1";
        
        // Mock 50 requests already in cache
        _cacheMock.Setup(x => x.GetAsync("fraud_v2_vel_1.1.1.1", default))
                  .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes("50"));

        // Act
        var result = await _service.CalculateRiskAsync(@event);

        // Assert
        // 51st request should trigger 80 risk score
        Assert.True(result.RiskScore >= 40); // Base weigthed score logic
        Assert.Contains("Extreme Velocity Burst", result.Reason);
    }
}

// Dummy Event Record for Testing if PageViewEvent is not visible
public record PageViewEvent : EventBase
{
    public override string EventType => "page_view";
    public string? Url { get; set; }
}
