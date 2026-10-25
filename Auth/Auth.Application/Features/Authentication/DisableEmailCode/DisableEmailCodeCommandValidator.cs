using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.DisableEmailCode;

/// <summary>
/// Validates the DisableEmailCodeCommand input: no code at all (ask for an email), a
/// six-digit emailed code, or any non-empty recovery code.
/// </summary>
public class DisableEmailCodeCommandValidator : AbstractValidator<DisableEmailCodeCommand>
{
    public DisableEmailCodeCommandValidator()
    {
        When(x => x.UseRecoveryCode,
            () => RuleFor(x => x.Code)
                .NotEmpty().WithErrorCode(TwoFactorErrors.RecoveryCodeRequired.Code))
            .Otherwise(() => RuleFor(x => x.Code!)
                .IsValidTwoFactorCode()
                .When(x => !string.IsNullOrEmpty(x.Code)));
    }
}
