using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Passkeys.CompletePasskeyRegistration;

/// <summary>
/// Validates the CompletePasskeyRegistrationCommand input fields.
/// </summary>
public sealed class CompletePasskeyRegistrationCommandValidator : AbstractValidator<CompletePasskeyRegistrationCommand>
{
    public CompletePasskeyRegistrationCommandValidator()
    {
        RuleFor(x => x.Name).IsValidPasskeyName();

        // Never required here: whether the bind needs the emailed code is the handler's
        // decision, from hot settings and the account's factors.
        RuleFor(x => x.EmailCode!)
            .IsValidTwoFactorEmailCode()
            .When(x => !string.IsNullOrEmpty(x.EmailCode));
    }
}
