using FluentValidation;

namespace Auth.Application.Features.WebhookKeys.RevokeWebhookKey;

/// <summary>
/// Validates the RevokeWebhookKeyCommand input fields.
/// </summary>
public class RevokeWebhookKeyCommandValidator : AbstractValidator<RevokeWebhookKeyCommand>
{
    public RevokeWebhookKeyCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Validation.WebhookKeyId.Required");
    }
}
