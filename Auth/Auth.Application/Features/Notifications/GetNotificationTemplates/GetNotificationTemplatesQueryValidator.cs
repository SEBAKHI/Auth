using Auth.Domain.Constants;
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
            .GreaterThanOrEqualTo(1).WithMessage("Validation.PageNumber.Min");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Validation.PageSize.Range");

        RuleFor(x => x.SortBy)
            .Must(sortBy => sortBy is null || SortFields.NotificationTemplates.Allowed.Contains(sortBy))
            .WithMessage("Validation.SortBy.NotAllowed");
    }
}
