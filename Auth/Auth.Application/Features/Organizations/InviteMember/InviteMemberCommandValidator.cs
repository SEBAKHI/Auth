using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Organizations.InviteMember;

/// <summary>
/// Validates the InviteMemberCommand input fields.
/// </summary>
public class InviteMemberCommandValidator : AbstractValidator<InviteMemberCommand>
{
    public InviteMemberCommandValidator()
    {
        RuleFor(x => x.Email).IsValidEmail();
    }
}
