using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(OrganizationErrors.IdRequired.Code);

        RuleFor(x => x.InvitationId)
            .NotEmpty().WithErrorCode(OrganizationErrors.InvitationIdRequired.Code);
    }
}
