using CloudScale.Shared.Events;

namespace CloudScale.Shared.Services;

public interface IRiskScoringStrategy
{
    Task<(int Score, string Reason)> CalculateRiskAsync(EventBase @event, IDictionary<string, object> context);
}
