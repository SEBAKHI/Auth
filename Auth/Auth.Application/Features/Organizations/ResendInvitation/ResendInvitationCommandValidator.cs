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
            .NotEmpty().WithMessage("Validation.OrganizationId.Required");

        RuleFor(x => x.InvitationId)
            .NotEmpty().WithMessage("Validation.InvitationId.Required");
    }
}
