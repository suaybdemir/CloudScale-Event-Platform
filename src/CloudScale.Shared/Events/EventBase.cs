using System;

namespace CloudScale.Shared.Events;

public interface IEvent
{
    string EventId { get; }
    string CorrelationId { get; }
    string TenantId { get; }
    string EventType { get; }
    DateTimeOffset CreatedAt { get; }
    string SchemaVersion { get; }
}

public abstract record EventBase : IEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString();
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? DeduplicationId { get; set; } // Formal Idempotency Key
    public string? PayloadHash { get; set; } // Principal Safeguard: Hash of event content
    public required string TenantId { get; init; }
    public string? UserId { get; init; }
    public double ConfidenceScore { get; set; } = 1.0; // Dynamic Confidence (0.0 - 1.0)
    public abstract string EventType { get; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string SchemaVersion { get; init; } = "1.0";
    public Dictionary<string, object> Metadata { get; init; } = new();
}
