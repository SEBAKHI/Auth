using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.VerifyEmailCodeSignIn;

/// <summary>
/// Only the code is validated, as in the registration's check: an unknown handle is
/// answered by the handler as an invalid or expired code.
/// </summary>
public class VerifyEmailCodeSignInCommandValidator : AbstractValidator<VerifyEmailCodeSignInCommand>
{
    public VerifyEmailCodeSignInCommandValidator()
    {
        RuleFor(x => x.Otp).IsValidEmailOtp();
    }
}
