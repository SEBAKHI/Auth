using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Organizations.RegisterWithInvitation;

/// <summary>
/// Validates the RegisterWithInvitationCommand input fields.
/// </summary>
public class RegisterWithInvitationCommandValidator : AbstractValidator<RegisterWithInvitationCommand>
{
    public RegisterWithInvitationCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).IsRequiredPassword();
        RuleFor(x => x.FirstName).IsValidFirstName();
        RuleFor(x => x.LastName).IsValidLastName();
    }
}
