using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.WebhookKeys.CreateWebhookKey;

/// <summary>
/// Validates the CreateWebhookKeyCommand input fields.
/// </summary>
public class CreateWebhookKeyCommandValidator : AbstractValidator<CreateWebhookKeyCommand>
{
    public CreateWebhookKeyCommandValidator()
    {
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
        RuleFor(x => x.TargetUrl).IsValidUrl();
        RuleFor(x => x.Environment)
            .NotEmpty().WithMessage("Validation.Environment.Required")
            .MaximumLength(50);
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow).WithMessage("Validation.ExpirationDate.Future")
            .When(x => x.ExpiresAt.HasValue);
    }
}
