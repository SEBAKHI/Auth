using FluentValidation;

namespace Auth.Application.Features.WebhookKeys.ValidateWebhookKey;

/// <summary>
/// Validates the ValidateWebhookKeyQuery input fields.
/// </summary>
public class ValidateWebhookKeyQueryValidator : AbstractValidator<ValidateWebhookKeyQuery>
{
    public ValidateWebhookKeyQueryValidator()
    {
        RuleFor(x => x.RawWebhookKey)
            .NotEmpty().WithMessage("Validation.WebhookKey.Required")
            .Must(key => key.StartsWith("wk_"))
            .WithMessage("Validation.WebhookKey.InvalidPrefix");
    }
}
