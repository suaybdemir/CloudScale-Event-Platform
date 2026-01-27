using Microsoft.Azure.Cosmos;
using Serilog;

namespace CloudScale.IngestionApi.Services
{
    public interface ISystemHealthProvider
    {
        bool IsThrottlingEnabled { get; }
    }

    public class SystemHealthWatcher : BackgroundService, ISystemHealthProvider
    {
        private readonly CosmosClient? _cosmosClient;
        private readonly IConfiguration _config;
        private bool _isThrottlingEnabled = false;

        public bool IsThrottlingEnabled => _isThrottlingEnabled;

        public SystemHealthWatcher(CosmosClient? cosmosClient, IConfiguration config)
        {
            _cosmosClient = cosmosClient;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_cosmosClient == null)
            {
                Log.Warning("SystemHealthWatcher disabled: CosmosClient is null");
                return;
            }

            var dbName = _config["CosmosDb:DatabaseName"] ?? "EventsDb";
            var containerName = _config["CosmosDb:ContainerName"] ?? "Events";
            var container = _cosmosClient.GetContainer(dbName, containerName);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var response = await container.ReadItemAsync<dynamic>(
                        "system:health", 
                        new PartitionKey("system"), 
                        cancellationToken: stoppingToken);

                    if (response?.Resource != null)
                    {
                        var resource = (IDictionary<string, object>)response.Resource;
                        if (resource.TryGetValue("IsUnderPressure", out var upObj) && upObj is bool underPressure)
                        {
                            if (_isThrottlingEnabled != underPressure)
                            {
                                _isThrottlingEnabled = underPressure;
                                if (_isThrottlingEnabled)
                                    Log.Warning("CRITICAL: Backpressure detected! API Throttling ENGAGED.");
                                else
                                    Log.Information("RECOVERY: Backpressure relieved. API Throttling DISENGAGED.");
                            }
                        }
                    }
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Not found is fine, might not have been created yet
                    _isThrottlingEnabled = false;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "SystemHealthWatcher failed to poll Cosmos DB");
                }

                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }
}
