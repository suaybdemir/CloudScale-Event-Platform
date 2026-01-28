using System.Net.Http.Json;
using CloudScale.Shared.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudScale.IngestionApi.Services;

/// <summary>
/// Principal Safeguard: Synthetic Transaction Monitoring (Canary Watchdog)
/// Periodically injects "known fraud" events to detect configuration drift or failure.
/// </summary>
public class CanaryWatchdogService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CanaryWatchdogService> _logger;
    private readonly string _canaryIp = "10.99.99.99"; // Known "Attacker" IP for config validation

    public CanaryWatchdogService(IHttpClientFactory httpClientFactory, ILogger<CanaryWatchdogService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Canary Watchdog started. Validating Security Configuration every 60s.");

        // Initial delay to let the system warm up
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCanaryValidationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Canary validation loop failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task RunCanaryValidationAsync()
    {
        var client = _httpClientFactory.CreateClient();
        
        // Get URL from Config or default to localhost (Docker service name or localhost)
        // In Docker, it might be http://ingestion-api:8080
        var baseUrl = "http://localhost:8080"; 
        
        // Check environment variable or configuration (Not injected in constructor to keep plain, but can use IConfiguration if needed)
        // Simplification: We assume localhost for now, but in Prod this should be the LoadBalancer URL.
        // Let's rely on standard config if available? But the service is inside the API itself calling itself?
        // If inside the API, localhost is fine. If this moves to a separate worker, it needs a URL.
        
        client.BaseAddress = new Uri(baseUrl); 

        // 1. Inject "Conscious Fraud" (Known Attacker IP)
        // Note: In a real system, the FraudDetectionService should have a specific rule for this IP
        // or this IP should be known to trigger velocity/travel alerts.
        bool shouldBeSuspicious = DateTime.UtcNow.Minute % 3 == 0;
        
        var canaryEvent = new {
            eventId = Guid.NewGuid().ToString(),
            eventType = "page_view",
            tenantId = "system-watchdog",
            correlationId = Guid.NewGuid().ToString(),
            userId = "canary-bot",
            url = "https://canary.system/health-check",
            metadata = new Dictionary<string, string> {
                { "ClientIp", _canaryIp },
                { "IsCanary", "true" },
                { "ForceSuspicious", shouldBeSuspicious.ToString().ToLower() }
            }
        };

        _logger.LogDebug("Sending Canary Event to validate fraud engine...");
        
        var response = await client.PostAsJsonAsync("/api/events", canaryEvent);
        
        // 2. Logic: The Fraud Engine should identify this as Risky.
        // For this prototype, we check if the system is at least alive.
        // Principal Refinement: Check if the response metadata or logs would indicate the block.
        // Since the actual 'blocking' (200 vs 403) isn't in the Ingestion API (it's async),
        // we monitor the 'FraudDetected' signal in the dashboard/logs.
        
        if (!response.IsSuccessStatusCode)
        {
             _logger.LogCritical("CANARY FAILURE: Ingestion API is not accepting events! Status: {Status}", response.StatusCode);
        }
        else
        {
            _logger.LogInformation("Canary Event Accepted by Ingestion. Monitoring Processor for detection...");
        }
    }
}
