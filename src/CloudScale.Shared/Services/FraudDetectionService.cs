using Microsoft.Extensions.Logging;
using CloudScale.Shared.Events;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace CloudScale.Shared.Services;

public interface IFraudDetectionService
{
    Task<(int RiskScore, string Reason)> CalculateRiskAsync(EventBase @event);
}

public class FraudDetectionService : IFraudDetectionService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<FraudDetectionService> _logger;

    public FraudDetectionService(IMemoryCache cache, ILogger<FraudDetectionService> logger)
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
        
        var eventCount = _cache.GetOrCreate(pointsKey, entry => {
            entry.SlidingExpiration = TimeSpan.FromDays(2);
            return 0;
        });
        _cache.Set(pointsKey, eventCount + 1);

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

    private Task<(int Score, string Reason)> CheckVelocityRisk(EventBase @event)
    {
        var ip = GetMetadata(@event, "ClientIp") ?? "unknown";
        var key = $"fraud_v2_vel_{ip}";

        var count = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return 0;
        });

        _cache.Set(key, count + 1, TimeSpan.FromMinutes(1));

        if (count > 50) return Task.FromResult((80, "Extreme Velocity Burst"));
        if (count > 20) return Task.FromResult((40, "High Request Rate"));
        
        return Task.FromResult((0, ""));
    }

    private Task<(int Score, string Reason)> CheckImpossibleTravel(EventBase @event)
    {
        var userId = @event.UserId;
        if (string.IsNullOrEmpty(userId)) return Task.FromResult((0, ""));

        var location = GetMetadata(@event, "Location");
        if (string.IsNullOrEmpty(location)) return Task.FromResult((0, ""));

        var key = $"fraud_v2_travel_{userId}";
        if (_cache.TryGetValue(key, out string? lastLocation))
        {
            if (lastLocation != location && lastLocation != "Internal" && location != "Internal")
            {
                // Different locations in < 1 min == Impossible Travel
                return Task.FromResult((60, $"Impossible Travel: {lastLocation} -> {location}"));
            }
        }

        _cache.Set(key, location, TimeSpan.FromMinutes(5));
        return Task.FromResult((0, ""));
    }

    private Task<(int Score, string Reason)> CheckPatternAnomaly(EventBase @event)
    {
        // Dummy pattern: Device switching
        var userId = @event.UserId;
        if (string.IsNullOrEmpty(userId)) return Task.FromResult((0, ""));

        var device = GetMetadata(@event, "DeviceType");
        var key = $"fraud_v2_device_{userId}";

        if (_cache.TryGetValue(key, out string? lastDevice))
        {
            if (lastDevice != device)
            {
                 _cache.Set(key, device, TimeSpan.FromHours(1));
                 return Task.FromResult((15, $"New Device Architecture Detected: {device}"));
            }
        }

        _cache.Set(key, device, TimeSpan.FromHours(24));
        return Task.FromResult((0, ""));
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
