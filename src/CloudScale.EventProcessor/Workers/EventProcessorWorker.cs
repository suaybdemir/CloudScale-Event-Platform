using Azure.Messaging.ServiceBus;
using CloudScale.EventProcessor.Configuration;
using CloudScale.EventProcessor.Services;
using CloudScale.Shared.Events;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Diagnostics;
using Polly;
using Polly.Retry;

namespace CloudScale.EventProcessor.Workers;

public class EventProcessorWorker : BackgroundService
{
    private readonly ServiceBusClient? _client;
    private readonly ServiceBusProcessor? _processor;
    private readonly ServiceBusSender? _sender; // For scheduling messages
    private readonly ICosmosDbService _cosmosService;
    private readonly CloudScale.Shared.Services.IFraudDetectionService _fraudService;
    private readonly IUserScoringService _scoringService;
    private readonly IArchiveService _archiveService;
    private readonly ILogger<EventProcessorWorker> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public EventProcessorWorker(
        ServiceBusClient? client, 
        ICosmosDbService cosmosService,
        CloudScale.Shared.Services.IFraudDetectionService fraudService,
        IUserScoringService scoringService,
        IArchiveService archiveService,
        IConfiguration config,
        ILogger<EventProcessorWorker> logger)
    {
        _client = client;
        _cosmosService = cosmosService;
        _fraudService = fraudService;
        _scoringService = scoringService;
        _archiveService = archiveService;
        _logger = logger;

        // Global Processing Resilience (Shared across all events)
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => 
                    ex is not JsonException && ex is not ArgumentException), // Don't retry poison pills
                OnRetry = args => {
                    _logger.LogWarning("Retrying message processing: attempt {Attempt}. Error: {Msg}", args.AttemptNumber, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        var queueName = config["ServiceBus:QueueName"];
        
        if (_client != null)
        {
            // Read parallelization settings from config (env vars)
            var maxConcurrent = int.TryParse(config["ServiceBus:MaxConcurrentCalls"], out var mc) ? mc : 16;
            var prefetch = int.TryParse(config["ServiceBus:PrefetchCount"], out var pf) ? pf : 100;
            
            var options = new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = maxConcurrent,
                PrefetchCount = prefetch,
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10)
            };
            
            _logger.LogInformation("ServiceBus Processor: MaxConcurrentCalls={MaxConcurrent}, PrefetchCount={Prefetch}", maxConcurrent, prefetch);

            _processor = _client.CreateProcessor(queueName, options);
            _sender = _client.CreateSender(queueName);
        }
        else
        {
            _logger.LogWarning("Event Processor running in MOCK mode (Idle)");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_processor == null) 
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        _processor.ProcessMessageAsync += MessageHandler;
        _processor.ProcessErrorAsync += ErrorHandler;

        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("Event Processor Started");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Event Processor Stopping");
        }
        finally
        {
            if (_processor != null)
            {
                await _processor.StopProcessingAsync();
                await _processor.DisposeAsync();
            }
        }
    }

    private async Task MessageHandler(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        var messageId = args.Message.MessageId;
        
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["MessageId"] = messageId,
            ["CorrelationId"] = args.Message.CorrelationId
        });

        _logger.LogDebug("Processing message {MessageId}", messageId);

        try
        {
            await _resiliencePipeline.ExecuteAsync(async _ => 
            {
                // 1. Determine Type
                string? eventType = args.Message.Subject;
                if (string.IsNullOrEmpty(eventType) && args.Message.ApplicationProperties.TryGetValue("EventType", out var et))
                {
                    eventType = et?.ToString();
                }

                if (string.IsNullOrEmpty(eventType))
                {
                    throw new ArgumentException($"Message {messageId} missing EventType.");
                }

                EventBase? eventBase = null;
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                eventBase = eventType switch
                {
                    "page_view" => JsonSerializer.Deserialize<PageViewEvent>(body, options),
                    "user_action" => JsonSerializer.Deserialize<UserActionEvent>(body, options),
                    "purchase" => JsonSerializer.Deserialize<PurchaseEvent>(body, options),
                    "check_cart_status" => JsonSerializer.Deserialize<CheckCartStatusEvent>(body, options),
                    _ => throw new ArgumentException($"Unknown event type: {eventType}")
                };

                if (eventBase == null) throw new JsonException("Deserialization resulted in null.");

                // 2. Intelligence Checks (Transformation, not Side-Effect)
                var riskResult = await _fraudService.CalculateRiskAsync(eventBase);
                eventBase.Metadata["RiskScore"] = riskResult.RiskScore;
                eventBase.Metadata["RiskReason"] = riskResult.Reason;
                eventBase.Metadata["IsSuspicious"] = riskResult.RiskScore >= 40;
                
                // --- SIDE-EFFECTS & ARCHIVE ---
                await _archiveService.ArchiveEventAsync(eventBase.EventId, body);

                // 3. Process (Save to Cosmos) 
                await _cosmosService.AddEventAsync(eventBase, args.CancellationToken);

                // 4. Alerting & Risk Signal Emission
                if (riskResult.RiskScore >= 40)
                {
                    _logger.LogWarning("High Risk Event processed: {Score} - {Reason}", riskResult.RiskScore, riskResult.Reason);
                }

                // 5. Downstream Analytics & Scoring
                await _scoringService.UpdateUserScoreAsync(eventBase, args.CancellationToken);

                // 6. Chain Reactions / Scheduling
                if (eventBase is UserActionEvent action && action.ActionName == "add_to_cart" && _sender != null)
                {
                    await ScheduleCartCheck(action, args.CancellationToken);
                }

                // Special Handling (System Events - already a downstream effect if sent via Schedule)
                if (eventBase is CheckCartStatusEvent checkEvent)
                {
                    await HandleCheckCartStatus(checkEvent, args);
                }
            }, args.CancellationToken);

            // 5. Complete
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Permanent failure for message {MessageId}. Moving to DLQ.", messageId);
             
             var reason = ex switch {
                 JsonException => "PoisonPill_Json",
                 ArgumentException => "PoisonPill_InvalidType",
                 _ => "ProcessingError"
             };

             var forensicDetails = new Dictionary<string, object>
             {
                 ["ErrorType"] = ex.GetType().Name,
                 ["FailedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                 ["RetryCount"] = args.Message.DeliveryCount
             };
             await args.DeadLetterMessageAsync(args.Message, forensicDetails, reason, ex.Message);
        }
    }

    private async Task HandleCheckCartStatus(CheckCartStatusEvent checkEvent, ProcessMessageEventArgs args)
    {
        _logger.LogInformation("Processing CheckCartStatus for {UserId}", checkEvent.UserId);

        if (args.Message.ApplicationProperties.TryGetValue("Since", out var sinceObj) && DateTimeOffset.TryParse(sinceObj.ToString(), out var since))
        {
            if (!string.IsNullOrEmpty(checkEvent.UserId))
            {
                var userId = checkEvent.UserId!;
                var hasPurchased = await _cosmosService.HasPurchaseAsync(userId, since, args.CancellationToken);
                if (!hasPurchased)
                {
                    _logger.LogWarning("ALERT: Cart Abandonment Detected for User {UserId}!", userId);
                }
            }
        }
    }

    private async Task ScheduleCartCheck(UserActionEvent action, CancellationToken ct)
    {
        var statusEvent = new CheckCartStatusEvent 
        { 
            CorrelationId = Guid.NewGuid().ToString(),
            CausationId = action.EventId,
            TenantId = action.TenantId,
            UserId = action.UserId ?? "unknown",
            CreatedAt = DateTimeOffset.UtcNow
        };
        
        var json = JsonSerializer.Serialize(statusEvent);
        var message = new ServiceBusMessage(json)
        {
            Subject = "check_cart_status",
            CorrelationId = statusEvent.CorrelationId,
            ApplicationProperties = 
            {
                { "EventType", "check_cart_status" },
                { "UserId", action.UserId ?? "" },
                { "Since", action.CreatedAt.ToString("O") }
            },
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        
        await _sender!.SendMessageAsync(message, ct);
        _logger.LogInformation("Scheduled cart check for User {UserId}", action.UserId);
    }

    private Task ErrorHandler(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus ErrorSource: {ErrorSource}, EntityPath: {EntityPath}", 
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
