using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Organizations.RegisterWithInvitation;

/// <summary>
/// Validates the RegisterWithInvitationCommand input fields.
/// </summary>
public class RegisterWithInvitationCommandValidator : AbstractValidator<RegisterWithInvitationCommand>
{
    public RegisterWithInvitationCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithErrorCode(OrganizationErrors.InvitationTokenRequired.Code);
        // Shape only: whether the password and the names are required is the
        // handler's, read from the Registration switches (OI-215).
        RuleFor(x => x.Password).IsOptionalPassword();
        RuleFor(x => x.FirstName).IsValidOptionalFirstName();
        RuleFor(x => x.LastName).IsValidOptionalLastName();
    }
}
