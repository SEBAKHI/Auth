using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.VerifyTwoFactorLogin;

/// <summary>
/// Validates the VerifyTwoFactorLoginCommand input fields.
/// </summary>
public class VerifyTwoFactorLoginCommandValidator : AbstractValidator<VerifyTwoFactorLoginCommand>
{
    public VerifyTwoFactorLoginCommandValidator()
    {
        RuleFor(x => x.ChallengeToken)
            .NotEmpty().WithErrorCode(TwoFactorErrors.ChallengeTokenRequired.Code);

        When(x => x.UseRecoveryCode,
            () => RuleFor(x => x.Code)
                .NotEmpty().WithErrorCode(TwoFactorErrors.RecoveryCodeRequired.Code))
            .Otherwise(() => RuleFor(x => x.Code).IsValidTwoFactorCode());
    }
}
