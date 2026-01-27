using FluentValidation;
using CloudScale.Shared.Events;

namespace CloudScale.Shared.Validation;

public class EventBaseValidator<T> : AbstractValidator<T> where T : EventBase
{
    public EventBaseValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.CorrelationId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.EventType).NotEmpty();
        RuleFor(x => x.SchemaVersion).NotEmpty();
    }
}

public class PageViewEventValidator : EventBaseValidator<PageViewEvent>
{
    public PageViewEventValidator()
    {
        RuleFor(x => x.Url).NotEmpty().Must(uri => Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out _))
            .WithMessage("Invalid URL format");
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public class UserActionEventValidator : EventBaseValidator<UserActionEvent>
{
    public UserActionEventValidator()
    {
        RuleFor(x => x.ActionName).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
