using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Notifications.GetNotificationTemplates;

/// <summary>
/// Validator for the notification template list query.
/// </summary>
public class GetNotificationTemplatesQueryValidator : AbstractValidator<GetNotificationTemplatesQuery>
{
    public GetNotificationTemplatesQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithErrorCode(PagingErrors.PageNumberOutOfRange.Code);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithErrorCode(PagingErrors.PageSizeOutOfRange.Code);

        RuleFor(x => x.SortBy)
            .Must(sortBy => sortBy is null || SortFields.NotificationTemplates.Allowed.Contains(sortBy))
            .WithErrorCode(SortingErrors.SortByNotAllowed.Code);
    }
}
