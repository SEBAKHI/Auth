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
            .NotEmpty().WithMessage("Validation.UserId.Required");

        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Validation.CurrentPassword.Required");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Validation.NewPassword.Required");

        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Validation.NewPassword.MustDiffer")
            .When(x => !string.IsNullOrEmpty(x.CurrentPassword) && !string.IsNullOrEmpty(x.NewPassword));
    }
}
