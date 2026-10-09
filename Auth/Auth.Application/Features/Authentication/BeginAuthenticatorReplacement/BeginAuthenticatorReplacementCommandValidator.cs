using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.BeginAuthenticatorReplacement;

/// <summary>
/// Validates the BeginAuthenticatorReplacementCommand input fields: a six-digit
/// authenticator code, or any non-empty recovery code.
/// </summary>
public class BeginAuthenticatorReplacementCommandValidator : AbstractValidator<BeginAuthenticatorReplacementCommand>
{
    public BeginAuthenticatorReplacementCommandValidator()
    {
        When(x => x.UseRecoveryCode,
            () => RuleFor(x => x.Code)
                .NotEmpty().WithErrorCode(TwoFactorErrors.RecoveryCodeRequired.Code))
            .Otherwise(() => RuleFor(x => x.Code).IsValidTwoFactorCode());
    }
}
