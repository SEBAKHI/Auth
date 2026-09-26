using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.ChangePassword;

/// <summary>
/// Validates the ChangePasswordCommand input fields.
/// </summary>
public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithErrorCode(UserErrors.IdRequired.Code);

        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithErrorCode(PasswordErrors.CurrentRequired.Code)
            .MaximumLength(PasswordLimits.MaxLength).WithErrorCode(PasswordErrors.CurrentTooLong.Code);

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithErrorCode(PasswordErrors.NewRequired.Code)
            .MaximumLength(PasswordLimits.MaxLength).WithErrorCode(PasswordErrors.NewTooLong.Code);

        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithErrorCode(PasswordErrors.NewMustDiffer.Code)
            .When(x => !string.IsNullOrEmpty(x.CurrentPassword) && !string.IsNullOrEmpty(x.NewPassword));

        RuleFor(x => x.ConfirmNewPassword)
            .Equal(x => x.NewPassword).WithErrorCode(PasswordErrors.ConfirmationMismatch.Code)
            .When(x => x.ConfirmNewPassword is not null);
    }
}
