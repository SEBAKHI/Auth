using FluentValidation;

namespace Auth.Application.Features.Authentication.ResetPassword;

/// <summary>
/// Validates the ResetPasswordCommand input fields.
/// </summary>
public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Validation.Email.Required")
            .EmailAddress().WithMessage("Validation.Email.InvalidFormat");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Validation.ResetToken.Required");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Validation.NewPassword.Required");
    }
}
