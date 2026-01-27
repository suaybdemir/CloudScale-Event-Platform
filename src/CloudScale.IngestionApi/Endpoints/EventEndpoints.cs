using CloudScale.IngestionApi.Services;
using CloudScale.Shared.Events;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CloudScale.IngestionApi.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events")
            .WithTags("Events");

        group.MapPost("/", async (
            [FromBody] JsonElement eventPayload,
            [FromServices] IServiceBusProducer producer,
            [FromServices] IEventEnrichmentService enricher,
            [FromServices] IStatsService stats,
            [FromServices] CloudScale.Shared.Services.IFraudDetectionService fraudService,
            [FromServices] IValidator<PageViewEvent> pageViewValidator,
            [FromServices] IValidator<UserActionEvent> userActionValidator,
            [FromServices] ISystemHealthProvider healthProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (healthProvider.IsThrottlingEnabled)
            {
                return Results.StatusCode(429);
            }

            if (!eventPayload.TryGetProperty("eventType", out var typeProp))
            {
                stats.RecordFailure();
                return Results.BadRequest(new { error = "Missing eventType" });
            }

            var eventType = typeProp.GetString();
            EventBase? eventToProcess = null;
            FluentValidation.Results.ValidationResult? validationResult = null;

            try 
            {
                var json = eventPayload.GetRawText();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                switch (eventType)
                {
                    case "page_view":
                        var pageEvent = JsonSerializer.Deserialize<PageViewEvent>(json, options);
                        if (pageEvent == null) return Results.BadRequest();
                        validationResult = await pageViewValidator.ValidateAsync(pageEvent, ct);
                        eventToProcess = pageEvent;
                        break;
                    
                    case "user_action":
                        var userEvent = JsonSerializer.Deserialize<UserActionEvent>(json, options);
                        if (userEvent == null) return Results.BadRequest();
                        validationResult = await userActionValidator.ValidateAsync(userEvent, ct);
                        eventToProcess = userEvent;
                        break;
                        
                    case "purchase":
                        var purchaseEvent = JsonSerializer.Deserialize<PurchaseEvent>(json, options);
                        if (purchaseEvent == null) return Results.BadRequest();
                        validationResult = await userActionValidator.ValidateAsync(purchaseEvent, ct);
                        eventToProcess = purchaseEvent;
                        break;
                        
                    case "check_cart_status":
                        var checkCartEvent = JsonSerializer.Deserialize<CheckCartStatusEvent>(json, options);
                        if (checkCartEvent == null) return Results.BadRequest();
                        eventToProcess = checkCartEvent;
                        break;
                        
                    default:
                        stats.RecordFailure();
                        return Results.BadRequest(new { error = $"Unknown event type: {eventType}" });
                }
            }
            catch (JsonException ex)
            {
                stats.RecordFailure();
                return Results.BadRequest(new { error = "Invalid JSON structure", details = ex.Message });
            }

            if (validationResult != null && !validationResult.IsValid)
            {
                stats.RecordFailure();
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            if (eventToProcess != null)
            {
                if (string.IsNullOrEmpty(eventToProcess.CorrelationId))
                {
                     eventToProcess = eventToProcess with { CorrelationId = context.TraceIdentifier };
                }
                
                enricher.Enrich(eventToProcess, context);
                
                // Real-time Fraud Check for Dashboard Feedback
                var risk = await fraudService.CalculateRiskAsync(eventToProcess);
                eventToProcess.Metadata["IsSuspicious"] = risk.RiskScore >= 40;
                eventToProcess.Metadata["RiskScore"] = risk.RiskScore;
                eventToProcess.Metadata["RiskReason"] = (string)risk.Reason;
                
                await producer.PublishAsync(eventToProcess, ct);
                
                stats.RecordEvent(eventToProcess);
                stats.RecordSuccess();
                
                return Results.Accepted(value: new { id = eventToProcess.EventId, correlationId = eventToProcess.CorrelationId });
            }

            stats.RecordFailure();
            return Results.BadRequest();
        })
        .WithName("SubmitEvent");

        group.MapPost("/batch", async (
            [FromBody] JsonElement[] eventsPayload,
            [FromServices] IServiceBusProducer producer,
            [FromServices] IEventEnrichmentService enricher,
            [FromServices] IStatsService stats,
            [FromServices] ISystemHealthProvider healthProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (healthProvider.IsThrottlingEnabled)
            {
                return Results.StatusCode(429);
            }

            var accepted = 0;
            var tasks = new List<Task>();

            foreach (var eventPayload in eventsPayload)
            {
                try
                {
                    if (!eventPayload.TryGetProperty("eventType", out var typeProp))
                    {
                        stats.RecordFailure();
                        continue;
                    }
                    
                    var json = eventPayload.GetRawText();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var eventType = typeProp.GetString();

                    EventBase? eventToProcess = eventType switch
                    {
                        "page_view" => JsonSerializer.Deserialize<PageViewEvent>(json, options),
                        "user_action" => JsonSerializer.Deserialize<UserActionEvent>(json, options),
                        "purchase" => JsonSerializer.Deserialize<PurchaseEvent>(json, options),
                        "check_cart_status" => JsonSerializer.Deserialize<CheckCartStatusEvent>(json, options),
                        _ => null
                    };

                    if (eventToProcess == null) continue;
                    
                    if (string.IsNullOrEmpty(eventToProcess.CorrelationId))
                    {
                        eventToProcess = eventToProcess with { CorrelationId = context.TraceIdentifier };
                    }

                    enricher.Enrich(eventToProcess, context);
                    tasks.Add(producer.PublishAsync(eventToProcess, ct));
                    
                    // RECORD THE EVENT IN STATS!
                    stats.RecordEvent(eventToProcess);
                    stats.RecordSuccess();
                    
                    accepted++;
                }
                catch 
                { 
                    stats.RecordFailure();
                }
            }

            await Task.WhenAll(tasks);
            return Results.Accepted(value: new { accepted, total = eventsPayload.Length });
        })
        .WithName("SubmitBatchEvents");
    }
}
