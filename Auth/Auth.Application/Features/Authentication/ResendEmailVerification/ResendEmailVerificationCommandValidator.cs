using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.ResendEmailVerification;

/// <summary>
/// Validates the ResendEmailVerificationCommand input fields.
/// </summary>
public class ResendEmailVerificationCommandValidator : AbstractValidator<ResendEmailVerificationCommand>
{
    public ResendEmailVerificationCommandValidator()
    {
        RuleFor(x => x.Email).IsValidEmail();
    }
}
