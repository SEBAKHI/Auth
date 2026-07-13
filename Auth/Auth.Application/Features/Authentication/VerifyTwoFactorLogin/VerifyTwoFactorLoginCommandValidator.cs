using Auth.Application.Validators.Rules;
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
            .NotEmpty().WithMessage("Validation.TwoFactorChallengeToken.Required");

        When(x => x.UseRecoveryCode,
            () => RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Validation.RecoveryCode.Required"))
            .Otherwise(() => RuleFor(x => x.Code).IsValidTotpCode());
    }
}
