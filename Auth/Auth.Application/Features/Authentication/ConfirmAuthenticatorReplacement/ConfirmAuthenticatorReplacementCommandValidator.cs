using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.ConfirmAuthenticatorReplacement;

/// <summary>
/// Validates the ConfirmAuthenticatorReplacementCommand input fields: a six-digit
/// code from the new authenticator app. A recovery code cannot confirm a new app —
/// only the app itself proves it was set up.
/// </summary>
public class ConfirmAuthenticatorReplacementCommandValidator : AbstractValidator<ConfirmAuthenticatorReplacementCommand>
{
    public ConfirmAuthenticatorReplacementCommandValidator()
    {
        RuleFor(x => x.Code).IsValidTwoFactorCode();
    }
}
