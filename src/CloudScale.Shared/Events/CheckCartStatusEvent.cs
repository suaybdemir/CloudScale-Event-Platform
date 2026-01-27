namespace CloudScale.Shared.Events;

public record CheckCartStatusEvent : EventBase
{
    public override string EventType => "check_cart_status";
}
