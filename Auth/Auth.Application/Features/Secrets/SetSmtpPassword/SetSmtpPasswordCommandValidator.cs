using FluentValidation;

namespace Auth.Application.Features.Secrets.SetSmtpPassword;

/// <summary>
/// Validates the SetSmtpPasswordCommand input fields.
/// </summary>
/// <remarks>
/// Length only: a mail provider may impose any character set, so anything
/// stricter would reject valid passwords. Correctness is proven by sending a
/// test message after the restart that puts the new value into effect.
/// </remarks>
public class SetSmtpPasswordCommandValidator : AbstractValidator<SetSmtpPasswordCommand>
{
    public SetSmtpPasswordCommandValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Validation.SecretValue.Required")
            .MaximumLength(512).WithMessage("Validation.SecretValue.MaxLength");
    }
}
