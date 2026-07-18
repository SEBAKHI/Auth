using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Notifications.GetNotificationOutboxMessages;

/// <summary>
/// Validator for the delivery-log list query.
/// </summary>
public class GetNotificationOutboxMessagesQueryValidator
    : AbstractValidator<GetNotificationOutboxMessagesQuery>
{
    public GetNotificationOutboxMessagesQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Validation.PageNumber.Min");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Validation.PageSize.Range");

        RuleFor(x => x.SortBy)
            .Must(sortBy => sortBy is null || SortFields.NotificationOutbox.Allowed.Contains(sortBy))
            .WithMessage("Validation.SortBy.NotAllowed");
    }
}
