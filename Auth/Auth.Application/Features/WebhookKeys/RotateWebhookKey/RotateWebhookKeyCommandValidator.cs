using FluentValidation;

namespace Auth.Application.Features.WebhookKeys.RotateWebhookKey;

/// <summary>
/// Validates the RotateWebhookKeyCommand input fields.
/// </summary>
public class RotateWebhookKeyCommandValidator : AbstractValidator<RotateWebhookKeyCommand>
{
    public RotateWebhookKeyCommandValidator()
    {
        RuleFor(x => x.GracePeriodMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Grace period must be 0 or greater.");
    }
}
