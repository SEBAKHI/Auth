using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.VerifyEmail;

/// <summary>
/// Validates the VerifyEmailCommand input fields.
/// </summary>
public class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.Otp).IsValidTotpCode();
    }
}
