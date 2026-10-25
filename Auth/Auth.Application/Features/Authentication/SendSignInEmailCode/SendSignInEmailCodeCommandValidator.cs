using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.SendSignInEmailCode;

/// <summary>
/// Validates the SendSignInEmailCodeCommand input fields.
/// </summary>
public class SendSignInEmailCodeCommandValidator : AbstractValidator<SendSignInEmailCodeCommand>
{
    public SendSignInEmailCodeCommandValidator()
    {
        RuleFor(x => x.ChallengeToken)
            .NotEmpty().WithErrorCode(TwoFactorErrors.ChallengeTokenRequired.Code);
    }
}
