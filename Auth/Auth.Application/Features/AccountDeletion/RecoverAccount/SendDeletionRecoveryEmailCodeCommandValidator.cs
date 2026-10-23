using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.RecoverAccount;

/// <summary>
/// Validates the SendDeletionRecoveryEmailCodeCommand input fields: the rules of the
/// recovery it precedes.
/// </summary>
public class SendDeletionRecoveryEmailCodeCommandValidator : AbstractValidator<SendDeletionRecoveryEmailCodeCommand>
{
    public SendDeletionRecoveryEmailCodeCommandValidator()
    {
        RuleFor(x => x.Email).IsValidEmail();

        RuleFor(x => x.Password)
            .NotEmpty().WithErrorCode(PasswordErrors.Required.Code)
            .MaximumLength(PasswordLimits.MaxLength).WithErrorCode(PasswordErrors.TooLong.Code);
    }
}
