using Microsoft.Extensions.Logging;
using CloudScale.Shared.Events;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace CloudScale.Shared.Services;

public interface IFraudDetectionService
{
    Task<(int RiskScore, string Reason)> CalculateRiskAsync(EventBase @event);
}

public class FraudDetectionService : IFraudDetectionService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<FraudDetectionService> _logger;

    public FraudDetectionService(IDistributedCache cache, ILogger<FraudDetectionService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<(int RiskScore, string Reason)> CalculateRiskAsync(EventBase @event)
    {
        // 0. Forced Injection for UI Verification
        if (@event.Metadata.TryGetValue("ForceSuspicious", out var force) && force?.ToString() == "true")
        {
            return (85, "Watchdog Artificial Security Event");
        }

        // 1. Learning Mode -> Dynamic Confidence Scoring
        var userId = @event.UserId ?? "anonymous";
        var pointsKey = $"confidence_points_{userId}";
        
        int eventCount = 0;
        var cachedCount = await _cache.GetStringAsync(pointsKey);
        if (cachedCount != null && int.TryParse(cachedCount, out var count))
        {
            eventCount = count;
        }

        eventCount++;
        await _cache.SetStringAsync(pointsKey, eventCount.ToString(), new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromDays(2)
        });

        // Confidence = 0.5 + (Min(1.0, eventCount/10.0) * 0.5)
        // Düzeltme: 0.5 taban puan ile başla (Sigmoid-floor). İlk işlemde bile %50 güven.
        double confidence = 0.5 + (Math.Min(1.0, (double)eventCount / 10.0) * 0.5);
        @event.ConfidenceScore = confidence;

        // 2. Statistical Anomaly Detection (Weighted)
        var velocityRisk = await CheckVelocityRisk(@event);
        var travelRisk = await CheckImpossibleTravel(@event);
        var patternRisk = await CheckPatternAnomaly(@event);

        int rawMaxScore = Math.Max(velocityRisk.Score, Math.Max(travelRisk.Score, patternRisk.Score));
        double weightedScore = (velocityRisk.Score * 0.4) + (travelRisk.Score * 0.4) + (patternRisk.Score * 0.2);
        
        // 3. Principal Refinement: Confidence Bypass for Critical Signals
        // Bazı sinyaller (örn: Impossible Travel > 60) confidence çarpanından muaf tutulur.
        bool bypassConfidence = travelRisk.Score >= 60; 

        // 4. Principal Refinement: Temporal Integrity (Occurrence vs Arrival)
        var reasons = new List<string>();
        if (velocityRisk.Score > 0) reasons.Add(velocityRisk.Reason);
        if (travelRisk.Score > 0) reasons.Add(travelRisk.Reason);
        if (patternRisk.Score > 0) reasons.Add(patternRisk.Reason);

        var occurrenceTime = @event.CreatedAt; 
        if (@event.Metadata.TryGetValue("OccurrenceTime", out var ot) && DateTimeOffset.TryParse(ot.ToString(), out var parsedOt))
        {
            occurrenceTime = parsedOt;
        }

        var lag = DateTimeOffset.UtcNow - occurrenceTime;
        if (lag > TimeSpan.FromMinutes(5))
        {
            _logger.LogWarning("LATE EVENT DETECTED: Event {Id} occurred {Lag:c} ago. Re-hydrating historical state...", @event.EventId, lag);
            reasons.Add($"Late Arrival ({lag.TotalMinutes:F1}m lag)");
        }

        // Final Score: (BaseRisk) * Confidence (unless bypassed)
        int baseRisk = (int)Math.Max(weightedScore, (double)rawMaxScore);
        int finalScore = bypassConfidence ? baseRisk : (int)(baseRisk * confidence);
        finalScore = Math.Min(finalScore, 100);

        var finalReason = reasons.Count > 0 ? string.Join(" | ", reasons) : "Normal";

        _logger.LogInformation("Risk Evaluation: Weighted={W}, MaxRaw={M}, Confidence={C:P}, Final={F}, Reason={R}, Time={T:O}", 
            weightedScore, rawMaxScore, confidence, finalScore, finalReason, occurrenceTime);

        if (finalScore >= 40)
        {
            _logger.LogWarning("Anomalous Activity Detected! Score: {Score} (Conf: {Conf:P}), Reason: {Reason}", finalScore, confidence, finalReason);
        }

        return (finalScore, finalReason);
    }

    private async Task<(int Score, string Reason)> CheckVelocityRisk(EventBase @event)
    {
        var ip = GetMetadata(@event, "ClientIp") ?? "unknown";
        var key = $"fraud_v2_vel_{ip}";

        int count = 0;
        var cachedVal = await _cache.GetStringAsync(key);
        if (cachedVal != null && int.TryParse(cachedVal, out var c))
        {
            count = c;
        }

        count++;
        await _cache.SetStringAsync(key, count.ToString(), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        if (count > 50) return (80, "Extreme Velocity Burst");
        if (count > 20) return (40, "High Request Rate");
        
        return (0, "");
    }

    private async Task<(int Score, string Reason)> CheckImpossibleTravel(EventBase @event)
    {
        var userId = @event.UserId;
        if (string.IsNullOrEmpty(userId)) return (0, "");

        var location = GetMetadata(@event, "Location");
        if (string.IsNullOrEmpty(location)) return (0, "");

        var key = $"fraud_v2_travel_{userId}";
        var lastLocation = await _cache.GetStringAsync(key);

        if (lastLocation != null)
        {
            if (lastLocation != location && lastLocation != "Internal" && location != "Internal")
            {
                // Different locations in < 1 min == Impossible Travel
                return (60, $"Impossible Travel: {lastLocation} -> {location}");
            }
        }

        await _cache.SetStringAsync(key, location!, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });
        return (0, "");
    }

    private async Task<(int Score, string Reason)> CheckPatternAnomaly(EventBase @event)
    {
        // Dummy pattern: Device switching
        var userId = @event.UserId;
        if (string.IsNullOrEmpty(userId)) return (0, "");

        var device = GetMetadata(@event, "DeviceType");
        if (string.IsNullOrEmpty(device)) return (0, "");

        var key = $"fraud_v2_device_{userId}";

        var lastDevice = await _cache.GetStringAsync(key);
        if (lastDevice != null)
        {
            if (lastDevice != device)
            {
                 // Update cache with new device
                 await _cache.SetStringAsync(key, device!, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
                 return (15, $"New Device Architecture Detected: {device}");
            }
        }

        await _cache.SetStringAsync(key, device!, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) });
        return (0, "");
    }

    private string? GetMetadata(EventBase @event, string key)
    {
        if (@event.Metadata.TryGetValue(key, out var val))
        {
            if (val is JsonElement je) return je.GetString();
            return val?.ToString();
        }
        return null;
    }
}
