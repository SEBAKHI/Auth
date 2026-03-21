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
            .NotEmpty().WithMessage("Webhook key is required.")
            .Must(key => key.StartsWith("wk_"))
            .WithMessage("Webhook key must start with a valid prefix (wk_).");
    }
}
