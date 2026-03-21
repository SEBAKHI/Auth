using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.EnableTwoFactor;

/// <summary>
/// Validates the EnableTwoFactorCommand input fields.
/// </summary>
public class EnableTwoFactorCommandValidator : AbstractValidator<EnableTwoFactorCommand>
{
    public EnableTwoFactorCommandValidator()
    {
        RuleFor(x => x.Code).IsValidTotpCode();
    }
}
