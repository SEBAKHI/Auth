using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.RecoverAccount;

/// <summary>
/// Validates the RecoverAccountCommand input fields.
/// </summary>
public class RecoverAccountCommandValidator : AbstractValidator<RecoverAccountCommand>
{
    public RecoverAccountCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Validation.Email.Required")
            .EmailAddress().WithMessage("Validation.Email.InvalidFormat");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Validation.Password.Required")
            .MaximumLength(PasswordLimits.MaxLength).WithMessage("Validation.Password.MaxLength");
    }
}
