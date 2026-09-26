using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.VerifyEmail;

/// <summary>
/// Validates the VerifyEmailCommand input fields.
/// </summary>
public class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.Otp).IsValidEmailOtp();

        RuleFor(x => x)
            .Must(x => x.UserId.HasValue || !string.IsNullOrWhiteSpace(x.Email))
            .WithErrorCode(EmailVerificationErrors.TargetRequired.Code);

        RuleFor(x => x.Email)
            .EmailAddress().WithErrorCode(EmailErrors.InvalidFormat.Code)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
