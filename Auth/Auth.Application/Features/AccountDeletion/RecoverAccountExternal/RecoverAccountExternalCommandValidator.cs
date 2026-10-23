using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.RecoverAccountExternal;

/// <summary>
/// Validates the RecoverAccountExternalCommand input fields.
/// </summary>
public class RecoverAccountExternalCommandValidator : AbstractValidator<RecoverAccountExternalCommand>
{
    public RecoverAccountExternalCommandValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty().WithErrorCode(ExternalAuthErrors.ProviderRequired.Code)
            .MaximumLength(50).WithErrorCode(ExternalAuthErrors.ProviderTooLong.Code);

        RuleFor(x => x.IdToken)
            .NotEmpty().WithErrorCode(ExternalAuthErrors.IdTokenRequired.Code);

        // Six digits (the authenticator app's code) or a recovery code as shown,
        // XXXX-XXXX (S24, ARR-1083): nothing longer is any code of the account.
        RuleFor(x => x.TwoFactorCode)
            .MaximumLength(TwoFactorCodeLimits.MaxLength).WithErrorCode(TwoFactorErrors.CodeTooLong.Code);
    }
}
