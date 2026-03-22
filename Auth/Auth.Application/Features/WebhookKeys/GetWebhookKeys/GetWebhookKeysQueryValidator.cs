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
            .NotEmpty().WithMessage("Application ID is required.");
    }
}
