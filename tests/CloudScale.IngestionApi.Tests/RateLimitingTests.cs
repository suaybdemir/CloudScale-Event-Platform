using CloudScale.IngestionApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace CloudScale.IngestionApi.Tests;

public class RateLimitingTests
{
    private readonly Mock<ILogger<RateLimitingMiddleware>> _loggerMock;
    private readonly Mock<IConfiguration> _configMock;

    public RateLimitingTests()
    {
        _loggerMock = new Mock<ILogger<RateLimitingMiddleware>>();
        _configMock = new Mock<IConfiguration>();
        
        // Setup config values explicitly to prevent "10000" override
        SetupConfig("RateLimiting:BurstCapacity", "100");
        SetupConfig("RateLimiting:TokensPerSecond", "10");
        SetupConfig("RateLimiting:GlobalPermitLimit", "10000");
    }

    private void SetupConfig(string key, string value)
    {
        var section = new Mock<IConfigurationSection>();
        section.Setup(x => x.Value).Returns(value);
        section.Setup(x => x.Path).Returns(key);
        _configMock.Setup(x => x.GetSection(key)).Returns(section.Object);
    }

    [Fact]
    public async Task Should_AllowRequest_WhenUnderLimit()
    {
        // Arrange
        var middleware = new RateLimitingMiddleware(
            next: (innerHttpContext) => Task.CompletedTask,
            _loggerMock.Object,
            _configMock.Object
        );

        var context = CreateHttpContext("192.168.1.1");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.NotEqual(429, context.Response.StatusCode);
    }

    [Fact]
    public async Task Should_Return429_WhenPerIpLimitExceeded()
    {
        // Arrange
        var middleware = new RateLimitingMiddleware(
            next: (innerHttpContext) => Task.CompletedTask,
            _loggerMock.Object,
            _configMock.Object
        );

        var clientIp = "10.0.0.100";
        int rateLimited = 0;

        // Act - Send 150 requests (limit is 100)
        for (int i = 0; i < 150; i++)
        {
            var context = CreateHttpContext(clientIp);
            await middleware.InvokeAsync(context);
            
            if (context.Response.StatusCode == 429)
            {
                rateLimited++;
            }
        }

        // Assert - At least 40 should be rate limited
        Assert.True(rateLimited >= 40, $"Expected at least 40 rate limited, got {rateLimited}");
    }

    [Fact]
    public async Task Should_IncludeRetryAfterHeader_When429()
    {
        // Arrange
        var middleware = new RateLimitingMiddleware(
            next: (innerHttpContext) => Task.CompletedTask,
            _loggerMock.Object,
            _configMock.Object
        );

        var clientIp = "10.0.0.200";
        HttpContext? limitedContext = null;

        // Act - Exhaust limit
        for (int i = 0; i < 150; i++)
        {
            var context = CreateHttpContext(clientIp);
            await middleware.InvokeAsync(context);
            
            if (context.Response.StatusCode == 429)
            {
                limitedContext = context;
                break;
            }
        }

        // Assert
        Assert.NotNull(limitedContext);
        Assert.True(limitedContext.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task Should_SkipRateLimiting_ForHealthEndpoint()
    {
        // Arrange
        var middleware = new RateLimitingMiddleware(
            next: (innerHttpContext) => Task.CompletedTask,
            _loggerMock.Object,
            _configMock.Object
        );

        // Act - Send many requests to /health
        for (int i = 0; i < 200; i++)
        {
            var context = CreateHttpContext("10.0.0.50", "/health");
            await middleware.InvokeAsync(context);
            
            // Assert - Should never be 429
            Assert.NotEqual(429, context.Response.StatusCode);
        }
    }

    private static HttpContext CreateHttpContext(string clientIp, string path = "/api/events")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "POST";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(clientIp.Split('.').Length == 4 ? clientIp : "127.0.0.1");
        context.Response.Body = new MemoryStream();
        return context;
    }
}
