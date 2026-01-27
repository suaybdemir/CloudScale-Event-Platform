using System.Collections.Generic;

namespace CloudScale.Shared.Events;

public record PurchaseEvent : UserActionEvent
{
    public override string EventType => "purchase";
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
}
