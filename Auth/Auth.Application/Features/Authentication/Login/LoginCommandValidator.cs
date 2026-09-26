using Auth.Domain.Constants;
using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(EmailErrors.Required.Code)
            .EmailAddress().WithErrorCode(EmailErrors.InvalidFormat.Code);

        RuleFor(x => x.Password)
            .NotEmpty().WithErrorCode(PasswordErrors.Required.Code)
            // Presented, not set — but still hashed if the account exists, so
            // the same ceiling applies before any work is done.
            .MaximumLength(PasswordLimits.MaxLength).WithErrorCode(PasswordErrors.TooLong.Code);
    }
}
