using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CloudScale.Integration.Tests;

public class SystemApiTests
{
    private readonly HttpClient _client;
    // Assuming the docker stack is running on localhost:5000 (Nginx) or 8080 (Direct)
    private const string BaseUrl = "http://localhost:5000"; 

    public SystemApiTests()
    {
        _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostIngest_WithoutApiKey_Returns401Unauthorized()
    {
        // Arrange
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/ingest", content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostIngest_WithValidPayload_Returns202Accepted()
    {
        // Arrange
        var payload = new
        {
            specversion = "1.0",
            type = "com.cloudscale.pageview", // Standard CloudEvents type
            source = "/test/runner",
            id = Guid.NewGuid().ToString(),
            userId = "test-user-1",
            url = "https://integration-test.com"
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        _client.DefaultRequestHeaders.Add("X-Api-Key", "dev-secret-key");
        
        // Act
        var response = await _client.PostAsync("/api/ingest", content);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostIngest_WithDuplicateId_Returns202Accepted_Idempotent()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var payload = new
        {
            type = "page_view",
            source = "/test/runner",
            id = id,
            url = "https://idempotency-test.com",
            userId = "test-user-2"
        };
        var json = JsonSerializer.Serialize(payload);
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "dev-secret-key");
        
        // Act 1
        var content1 = new StringContent(json, Encoding.UTF8, "application/json");
        var response1 = await _client.PostAsync("/api/ingest", content1);
        
        // Act 2 (Retry)
        var content2 = new StringContent(json, Encoding.UTF8, "application/json");
        var response2 = await _client.PostAsync("/api/ingest", content2);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response1.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, response2.StatusCode);
    }
}
