using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.CompleteRegistration;

/// <summary>
/// Shape only. The password's policy (length, classes, breach) is the
/// handler's, applied AFTER the code has been checked, so a stranger with a
/// handle learns nothing about the policy and spends nothing on the hasher.
/// </summary>
public class CompleteRegistrationCommandValidator : AbstractValidator<CompleteRegistrationCommand>
{
    public CompleteRegistrationCommandValidator()
    {
        RuleFor(x => x.Otp).IsValidTotpCode();
        RuleFor(x => x.Password).IsRequiredPassword();
        RuleFor(x => x.FirstName).IsValidFirstName();
        RuleFor(x => x.LastName).IsValidLastName();
        RuleFor(x => x.TimeZone).IsValidTimeZone();
    }
}
