using Auth.Domain.Errors;
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
            .MaximumLength(50).WithErrorCode(OrganizationErrors.SubscriptionTierTooLong.Code)
            .When(x => x.SubscriptionTier is not null);

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow)
            .WithErrorCode(ExpiryErrors.NotInFuture.Code)
            .When(x => x.ExpiresAt is not null);
    }
}
