using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.StepUpTwoFactor;

/// <summary>
/// Validates the StepUpTwoFactorCommand input fields: a six-digit authenticator
/// code, or any non-empty recovery code — the rule sign-in applies to the same pair.
/// </summary>
public class StepUpTwoFactorCommandValidator : AbstractValidator<StepUpTwoFactorCommand>
{
    public StepUpTwoFactorCommandValidator()
    {
        When(x => x.UseRecoveryCode,
            () => RuleFor(x => x.Code)
                .NotEmpty().WithErrorCode(TwoFactorErrors.RecoveryCodeRequired.Code))
            .Otherwise(() => RuleFor(x => x.Code).IsValidTwoFactorCode());
    }
}
