using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.ResetPassword;

/// <summary>
/// Validates the ResetPasswordCommand input fields.
/// </summary>
public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithErrorCode(PasswordResetErrors.TokenRequired.Code);

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithErrorCode(PasswordErrors.NewRequired.Code)
            .MaximumLength(PasswordLimits.MaxLength).WithErrorCode(PasswordErrors.NewTooLong.Code);

        RuleFor(x => x.ConfirmNewPassword)
            .Equal(x => x.NewPassword).WithErrorCode(PasswordErrors.ConfirmationMismatch.Code)
            .When(x => x.ConfirmNewPassword is not null);
    }
}
