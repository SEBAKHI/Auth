using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(WebhookKeyErrors.Required.Code)
            .Must(key => key.StartsWith("wk_"))
            .WithErrorCode(WebhookKeyErrors.InvalidPrefix.Code);
    }
}
