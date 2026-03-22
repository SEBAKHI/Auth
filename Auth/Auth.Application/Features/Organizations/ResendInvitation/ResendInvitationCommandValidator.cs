using FluentValidation;

namespace Auth.Application.Features.Organizations.ResendInvitation;

/// <summary>
/// Validator for the resend invitation command.
/// </summary>
public class ResendInvitationCommandValidator : AbstractValidator<ResendInvitationCommand>
{
    public ResendInvitationCommandValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("Organization ID is required.");

        RuleFor(x => x.InvitationId)
            .NotEmpty().WithMessage("Invitation ID is required.");
    }
}
