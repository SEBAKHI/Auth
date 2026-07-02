using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.WebhookKeys.GetWebhookKeys;

/// <summary>
/// Validates the GetWebhookKeysQuery input fields.
/// </summary>
public class GetWebhookKeysQueryValidator : AbstractValidator<GetWebhookKeysQuery>
{
    public GetWebhookKeysQueryValidator()
    {
        RuleFor(x => x.ApplicationId)
            .NotEmpty().WithMessage("Validation.ApplicationId.Required");
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.WebhookKeys.Allowed);
    }
}
