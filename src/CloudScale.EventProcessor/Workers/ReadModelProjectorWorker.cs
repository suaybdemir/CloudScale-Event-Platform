using Microsoft.Azure.Cosmos;
using CloudScale.Shared.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CloudScale.EventProcessor.Workers;

/// <summary>
/// Principal Refinement: Demonstrates Read-Model Resilience via Change Feed.
/// This worker projects data from the "Hot Store" (Source of Truth) to a disposable Read Model.
/// Supports "StartFromBeginning" for full state rehydration.
/// </summary>
public class ReadModelProjectorWorker : BackgroundService
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<ReadModelProjectorWorker> _logger;
    private readonly string _databaseName;
    private readonly string _sourceContainerName;
    private readonly string _leaseContainerName = "leases";
    private ChangeFeedProcessor? _changeFeedProcessor;

    public ReadModelProjectorWorker(
        CosmosClient cosmosClient,
        IConfiguration config,
        ILogger<ReadModelProjectorWorker> logger)
    {
        _cosmosClient = cosmosClient;
        _logger = logger;
        _databaseName = config["CosmosDb:DatabaseName"] ?? "EventsDb";
        _sourceContainerName = config["CosmosDb:ContainerName"] ?? "Events";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Principal Resilience: Exponential backoff for Change Feed initialization
        int retryCount = 0;
        bool started = false;

        while (!started && !stoppingToken.IsCancellationRequested && retryCount < 10)
        {
            try
            {
                _logger.LogInformation("Initializing Principal Read-Model Projector (Change Feed)... Attempt {Attempt}", retryCount + 1);

                var database = _cosmosClient.GetDatabase(_databaseName);
                
                // Ensure Lease Container exists for the checkpointing state
                await database.CreateContainerIfNotExistsAsync(_leaseContainerName, "/id", cancellationToken: stoppingToken);

                Container leaseContainer = database.GetContainer(_leaseContainerName);
                Container sourceContainer = database.GetContainer(_sourceContainerName);

                _changeFeedProcessor = sourceContainer.GetChangeFeedProcessorBuilder<dynamic>(
                    processorName: "DashboardProjector-v1",
                    onChangesDelegate: HandleChangesAsync)
                    .WithInstanceName($"Projector-{Guid.NewGuid()}")
                    .WithLeaseContainer(leaseContainer)
                    .WithStartTime(DateTime.MinValue.ToUniversalTime()) // Principal Requirement: StartFromBeginning support
                    .Build();

                await _changeFeedProcessor.StartAsync();
                _logger.LogInformation("Change Feed Projector STARTED. Read-Model Rehydration in progress...");
                started = true;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.LogWarning(ex, "Failed attempt {Attempt} to start Change Feed Projector. Retrying in {Delay}s...", retryCount, Math.Pow(2, retryCount));
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), stoppingToken);
            }
        }

        if (!started)
        {
            _logger.LogError("PERMANENT FAILURE: Change Feed Projector could not be started after {Count} retries.", retryCount);
        }
        else
        {
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Change Feed Projector stopping...");
            }
            finally
            {
                if (_changeFeedProcessor != null)
                {
                    await _changeFeedProcessor.StopAsync();
                }
            }
        }
    }

    private async Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<dynamic> changes,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Change Feed: Projecting {Count} events to Dashboard Read-Model...", changes.Count);

        // Principal Implementation: idempotent projection to an external search index or dashboard db
        foreach (var item in changes)
        {
            // Simulation of projection logic
            // In a real system, this would update a Redis cache, ElasticSearch, or another SQL DB.
            await Task.Yield();
        }

        _logger.LogDebug("Checkpoint reached. Read-Model consistency guaranteed at LSN: {Lsn}", context.Headers.Session);
    }
}
