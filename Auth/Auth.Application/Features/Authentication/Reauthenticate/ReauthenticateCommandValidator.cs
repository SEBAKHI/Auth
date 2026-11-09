using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.Reauthenticate;

/// <summary>
/// Validates the ReauthenticateCommand input fields: one first proof, in its shape, under the
/// ceiling a presented password has at sign-in, since it is hashed. Whether the account takes that
/// proof, and whether a second step must follow, is the handler's to say.
/// </summary>
public class ReauthenticateCommandValidator : AbstractValidator<ReauthenticateCommand>
{
    /// <summary>The longest recovery code or authenticator code worth reading.</summary>
    private const int SecondStepCodeMaxLength = 64;

    public ReauthenticateCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.Password) || !string.IsNullOrEmpty(x.EmailCode))
            .WithErrorCode(PasswordErrors.Required.Code);

        RuleFor(x => x.Password)
            .MaximumLength(PasswordLimits.MaxLength).WithErrorCode(PasswordErrors.TooLong.Code);

        When(x => !string.IsNullOrEmpty(x.EmailCode), () => RuleFor(x => x.EmailCode!).IsValidTwoFactorCode());

        RuleFor(x => x.SecondStepCode)
            .MaximumLength(SecondStepCodeMaxLength).WithErrorCode(UserErrors.InvalidTwoFactorCode.Code);
    }
}
