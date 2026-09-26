using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.RecoverAccount;

/// <summary>
/// Validates the RecoverAccountCommand input fields.
/// </summary>
public class RecoverAccountCommandValidator : AbstractValidator<RecoverAccountCommand>
{
    public RecoverAccountCommandValidator()
    {
        RuleFor(x => x.Email).IsValidEmail();

        RuleFor(x => x.Password)
            .NotEmpty().WithErrorCode(PasswordErrors.Required.Code)
            .MaximumLength(PasswordLimits.MaxLength).WithErrorCode(PasswordErrors.TooLong.Code);
    }
}
