using FluentValidation;

namespace Auth.Application.Features.Authentication.Login;

/// <summary>
/// Validates the LoginCommand input fields.
/// </summary>
public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Validation.Email.Required")
            .EmailAddress().WithMessage("Validation.Email.InvalidFormat");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Validation.Password.Required");
    }
}
