using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
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

        // An absent method keeps today's meaning; a present one must be a known name.
        RuleFor(x => x.Method)
            .Must(TwoFactorMethodNames.IsKnown).WithErrorCode(TwoFactorErrors.MethodInvalid.Code)
            .When(x => x.Method is not null);

        // A recovery code is free-form; the authenticator app's code and the emailed
        // code are six digits.
        When(x => x.Method is null || TwoFactorMethodNames.IsKnown(x.Method), () =>
            When(x => x.PresentedMethod == SecondFactorMethod.RecoveryCode,
                () => RuleFor(x => x.Code)
                    .NotEmpty().WithErrorCode(TwoFactorErrors.RecoveryCodeRequired.Code))
                .Otherwise(() => RuleFor(x => x.Code).IsValidTwoFactorCode()));
    }
}
