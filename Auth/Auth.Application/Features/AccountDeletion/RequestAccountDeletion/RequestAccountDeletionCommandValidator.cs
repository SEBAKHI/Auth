using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.RequestAccountDeletion;

/// <summary>
/// Validates the RequestAccountDeletionCommand input fields. Identical shape to
/// <c>ConfirmPublicDeletionCommandValidator</c> minus the email, which the
/// authenticated caller does not supply.
/// </summary>
public class RequestAccountDeletionCommandValidator : AbstractValidator<RequestAccountDeletionCommand>
{
    public RequestAccountDeletionCommandValidator()
    {
        RuleFor(x => x.OtpCode)
            .NotEmpty().WithErrorCode(AccountDeletionErrors.OtpCodeRequired.Code)
            .Length(6).WithErrorCode(AccountDeletionErrors.OtpCodeInvalidFormat.Code);
    }
}
