using FluentValidation;

namespace Auth.Application.Features.Organizations.EnableApplication;

/// <summary>
/// Validates the EnableApplicationCommand input fields.
/// </summary>
public class EnableApplicationCommandValidator : AbstractValidator<EnableApplicationCommand>
{
    public EnableApplicationCommandValidator()
    {
        RuleFor(x => x.SubscriptionTier)
            .MaximumLength(50)
            .When(x => x.SubscriptionTier is not null);

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow)
            .WithMessage("Validation.ExpirationDate.Future")
            .When(x => x.ExpiresAt is not null);
    }
}
