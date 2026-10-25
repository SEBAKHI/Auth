using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.ConfirmEmailCode;

/// <summary>
/// Validates the ConfirmEmailCodeCommand input: the six-digit emailed code, in the
/// <c>code</c> member, under the rule every six-digit <c>code</c> has.
/// </summary>
public class ConfirmEmailCodeCommandValidator : AbstractValidator<ConfirmEmailCodeCommand>
{
    public ConfirmEmailCodeCommandValidator()
    {
        RuleFor(x => x.Code).IsValidTwoFactorCode();
    }
}
