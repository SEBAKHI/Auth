using FluentValidation;

namespace Auth.Application.Features.Organizations.UpdateOrganizationApplication;

/// <summary>
/// Validates the UpdateOrganizationApplicationCommand input fields.
/// </summary>
public class UpdateOrganizationApplicationCommandValidator : AbstractValidator<UpdateOrganizationApplicationCommand>
{
    public UpdateOrganizationApplicationCommandValidator()
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
