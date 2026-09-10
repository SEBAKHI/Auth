using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.VerifyRegistration;

/// <summary>
/// Only the code is validated here. The handle is an opaque keyed digest that
/// the request contract already bounds (required, at most 100 characters); a
/// value that matches no row is answered by the handler as an invalid or
/// expired code, which is the one answer every unknown handle must get.
/// </summary>
public class VerifyRegistrationCommandValidator : AbstractValidator<VerifyRegistrationCommand>
{
    public VerifyRegistrationCommandValidator()
    {
        RuleFor(x => x.Otp).IsValidTotpCode();
    }
}
