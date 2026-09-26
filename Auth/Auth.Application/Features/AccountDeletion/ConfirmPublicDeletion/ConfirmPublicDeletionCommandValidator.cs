using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.ConfirmPublicDeletion;

/// <summary>
/// Validates the ConfirmPublicDeletionCommand input fields.
/// </summary>
public class ConfirmPublicDeletionCommandValidator : AbstractValidator<ConfirmPublicDeletionCommand>
{
    public ConfirmPublicDeletionCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithErrorCode(EmailErrors.Required.Code)
            .EmailAddress().WithErrorCode(EmailErrors.InvalidFormat.Code);

        RuleFor(x => x.OtpCode)
            .NotEmpty().WithErrorCode(AccountDeletionErrors.OtpCodeRequired.Code)
            .Length(6).WithErrorCode(AccountDeletionErrors.OtpCodeInvalidFormat.Code);
    }
}
