using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.DisableTwoFactor;

/// <summary>
/// Validates the DisableTwoFactorCommand input fields.
/// </summary>
public class DisableTwoFactorCommandValidator : AbstractValidator<DisableTwoFactorCommand>
{
    public DisableTwoFactorCommandValidator()
    {
        RuleFor(x => x.Code).IsValidTwoFactorCode();
    }
}
