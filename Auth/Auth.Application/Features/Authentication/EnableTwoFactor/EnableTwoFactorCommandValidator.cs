using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.EnableTwoFactor;

/// <summary>
/// Validates the EnableTwoFactorCommand input fields.
/// </summary>
public class EnableTwoFactorCommandValidator : AbstractValidator<EnableTwoFactorCommand>
{
    public EnableTwoFactorCommandValidator()
    {
        RuleFor(x => x.Code).IsValidTwoFactorCode();

        // Never required here: whether the bind needs the emailed code is the
        // handler's decision, from hot settings, and a client that sent none gets
        // TwoFactor.EmailCodeRequired from it rather than a generic validation code.
        RuleFor(x => x.EmailCode!)
            .IsValidTwoFactorEmailCode()
            .When(x => !string.IsNullOrEmpty(x.EmailCode));
    }
}
