using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Organizations.AcceptInvitation;

/// <summary>
/// Validates the AcceptInvitationCommand input fields.
/// </summary>
public class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(x => x.Token).IsRequiredToken();
    }
}
