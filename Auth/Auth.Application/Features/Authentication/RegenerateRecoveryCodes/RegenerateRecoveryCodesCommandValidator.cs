using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.RegenerateRecoveryCodes;

/// <summary>
/// Validates the RegenerateRecoveryCodesCommand input fields: a six-digit
/// authenticator code, or any non-empty recovery code — the rule sign-in applies
/// to the same pair.
/// </summary>
public class RegenerateRecoveryCodesCommandValidator : AbstractValidator<RegenerateRecoveryCodesCommand>
{
    public RegenerateRecoveryCodesCommandValidator()
    {
        When(x => x.UseRecoveryCode,
            () => RuleFor(x => x.Code)
                .NotEmpty().WithErrorCode(TwoFactorErrors.RecoveryCodeRequired.Code))
            .Otherwise(() => RuleFor(x => x.Code).IsValidTwoFactorCode());
    }
}
