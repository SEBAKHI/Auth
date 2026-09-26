using Auth.Domain.Constants;
using Auth.Domain.Errors;
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
            .GreaterThanOrEqualTo(1).WithErrorCode(PagingErrors.PageNumberOutOfRange.Code);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithErrorCode(PagingErrors.PageSizeOutOfRange.Code);

        RuleFor(x => x.SortBy)
            .Must(sortBy => sortBy is null || SortFields.NotificationOutbox.Allowed.Contains(sortBy))
            .WithErrorCode(SortingErrors.SortByNotAllowed.Code);
    }
}
