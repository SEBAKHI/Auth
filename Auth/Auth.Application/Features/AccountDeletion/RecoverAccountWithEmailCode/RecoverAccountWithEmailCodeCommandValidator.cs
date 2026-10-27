using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.RecoverAccountWithEmailCode;

/// <summary>
/// Validates the RecoverAccountWithEmailCodeCommand input fields.
/// </summary>
public class RecoverAccountWithEmailCodeCommandValidator : AbstractValidator<RecoverAccountWithEmailCodeCommand>
{
    public RecoverAccountWithEmailCodeCommandValidator()
    {
        RuleFor(x => x.Otp).IsValidEmailOtp();

        // Six digits (the authenticator app's code) or a recovery code as shown: nothing
        // longer is any code of the account.
        RuleFor(x => x.TwoFactorCode)
            .MaximumLength(TwoFactorCodeLimits.MaxLength).WithErrorCode(TwoFactorErrors.CodeTooLong.Code);
    }
}
