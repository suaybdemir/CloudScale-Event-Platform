using Microsoft.Azure.Cosmos;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using CloudScale.IngestionApi.Services;

namespace CloudScale.IngestionApi.Endpoints;

public static class ReadEndpoints
{
    public static void MapReadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard").WithTags("Dashboard");

        group.MapGet("/stats", ([FromServices] IStatsService stats) =>
        {
            var (total, fraud) = stats.GetCounts();
            return Results.Ok(new 
            { 
                totalEvents = total, 
                fraudCount = fraud,
                timestamp = DateTime.UtcNow
            });
        });

        group.MapGet("/detailed-stats", ([FromServices] IStatsService stats) =>
        {
            return Results.Ok(stats.GetDetailedStats());
        });

        group.MapGet("/alerts", ([FromServices] IStatsService stats) =>
        {
            return Results.Ok(stats.GetRecentAlerts());
        });

        group.MapGet("/top-users", ([FromServices] IStatsService stats) =>
        {
            return Results.Ok(stats.GetTopUsers());
        });

        group.MapGet("/audit-log", ([FromServices] IStatsService stats) => 
        {
             // Use the in-memory recent event history for robustness in demo env
             return Results.Ok(stats.GetRecentEvents());
        });

        group.MapGet("/debug-query", async ([FromServices] CosmosClient client, IConfiguration config) => 
        {
             var dbName = config["CosmosDb:DatabaseName"] ?? "EventsDb";
             var container = client.GetContainer(dbName, "Events");
             
             // Try Single Partition Query
             var pk = new PartitionKey("manual:2026-01");
             var query = new QueryDefinition("SELECT * FROM c");
             
             using var iter = container.GetItemQueryIterator<dynamic>(query, requestOptions: new QueryRequestOptions { PartitionKey = pk });
             
             var results = new List<object>();
             if (iter.HasMoreResults)
             {
                 var response = await iter.ReadNextAsync(); // Should not hang?
                 results.AddRange(response);
             }
             
             return Results.Ok(new { status = "Success", count = results.Count });
        });
    }
    
    // DTOs for Cosmos deserialization
    private record AlertDoc
    {
        public string? id { get; init; }
        public EventDataDto? EventData { get; init; }
    }
    
    private record EventDataDto
    {
        public string? EventType { get; init; }
        public string? UserId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }
    
    private record UserProfileDoc
    {
        public string? id { get; init; }
        public int totalScore { get; init; }
        public int eventCount { get; init; }
    }
}
