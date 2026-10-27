using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.SignInWithEmailCode;

/// <summary>Only the code's shape: an unknown handle is answered as an invalid or expired code.</summary>
public class SignInWithEmailCodeCommandValidator : AbstractValidator<SignInWithEmailCodeCommand>
{
    public SignInWithEmailCodeCommandValidator()
    {
        RuleFor(x => x.Otp).IsValidEmailOtp();
    }
}
