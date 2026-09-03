using Auth.Domain.Constants;
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
            .NotEmpty().WithMessage("Validation.Password.Required")
            // Presented, not set — but still hashed if the account exists, so
            // the same ceiling applies before any work is done.
            .MaximumLength(PasswordLimits.MaxLength).WithMessage("Validation.Password.MaxLength");
    }
}
