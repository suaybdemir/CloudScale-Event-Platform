using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using Moq;
using CloudScale.IngestionApi; // For Program

namespace CloudScale.Integration.Tests;

public class SystemApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public SystemApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // 1. Remove Real Service Bus
                services.RemoveAll<ServiceBusClient>();
                services.RemoveAll<ServiceBusSender>();

                // 2. Mock Service Bus
                var mockSbClient = new Mock<ServiceBusClient>();
                var mockSbSender = new Mock<ServiceBusSender>();
                
                mockSbClient.Setup(x => x.CreateSender(It.IsAny<string>()))
                            .Returns(mockSbSender.Object);
                
                mockSbClient.Setup(x => x.CreateSender(It.IsAny<string>(), It.IsAny<ServiceBusSenderOptions>()))
                            .Returns(mockSbSender.Object);

                mockSbSender.Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

                services.AddSingleton(mockSbClient.Object);

                // 3. Remove Real Cosmos
                services.RemoveAll<CosmosClient>();
                var mockCosmos = new Mock<CosmosClient>();
                var mockContainer = new Mock<Container>();

                // Setup GetContainer to prevent NPE in SystemHealthWatcher
                mockCosmos.Setup(x => x.GetContainer(It.IsAny<string>(), It.IsAny<string>()))
                          .Returns(mockContainer.Object);
                
                // Mock ReadItemAsync to throw NotFound (simulating no health override)
                // or return a dummy item. Thowing NotFound is safer path in SystemHealthWatcher logic.
                mockContainer.Setup(x => x.ReadItemAsync<dynamic>(
                        It.IsAny<string>(), 
                        It.IsAny<PartitionKey>(), 
                        It.IsAny<ItemRequestOptions>(), 
                        It.IsAny<CancellationToken>()))
                      .ThrowsAsync(new CosmosException("Not Found", HttpStatusCode.NotFound, 0, "0", 0));

                services.AddSingleton(mockCosmos.Object);
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostIngest_WithoutApiKey_Returns401Unauthorized()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/ingest", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostIngest_WithValidPayload_Returns202Accepted()
    {
        var payload = new
        {
            specversion = "1.0",
            type = "com.cloudscale.pageview",
            source = "/test/runner",
            id = Guid.NewGuid().ToString(),
            // REQUIRED FIELDS FOR VALIDATION
            tenantId = "test-tenant-1",
            correlationId = Guid.NewGuid().ToString(),
            // Payload Data
            userId = "test-user-1",
            url = "https://integration-test.com"
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        _client.DefaultRequestHeaders.Add("X-Api-Key", "dev-secret-key");
        
        var response = await _client.PostAsync("/api/ingest", content);
        
        // Debug output if fails
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Status: {response.StatusCode}, Details: {err}");
        }

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostIngest_WithDuplicateId_Returns202Accepted_Idempotent()
    {
        var id = Guid.NewGuid().ToString();
        var payload = new
        {
            specversion = "1.0",
            type = "com.cloudscale.pageview",
            source = "/test/runner",
            id = id,
            // REQUIRED FIELDS
            tenantId = "test-tenant-2",
            correlationId = Guid.NewGuid().ToString(),
            // Payload Data
            userId = "test-user-2",
            url = "https://idempotency-test.com"
        };
        var json = JsonSerializer.Serialize(payload);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "dev-secret-key");
        
        var content1 = new StringContent(json, Encoding.UTF8, "application/json");
        var response1 = await _client.PostAsync("/api/ingest", content1);
        
        var content2 = new StringContent(json, Encoding.UTF8, "application/json");
        var response2 = await _client.PostAsync("/api/ingest", content2);

        Assert.Equal(HttpStatusCode.Accepted, response1.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, response2.StatusCode);
    }
}
