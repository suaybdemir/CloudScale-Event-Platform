namespace CloudScale.Shared.Events;

public record PageViewEvent : EventBase
{
    public override string EventType => "page_view";
    public required string Url { get; init; }
    public string? Referrer { get; init; }
    public string? UserAgent { get; init; }
}

public record UserActionEvent : EventBase
{
    public override string EventType => "user_action";
    public required string ActionName { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();
}
