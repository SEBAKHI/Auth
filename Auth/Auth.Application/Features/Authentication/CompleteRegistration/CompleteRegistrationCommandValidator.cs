using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.CompleteRegistration;

/// <summary>
/// Shape only. The password's policy (length, classes, breach) is the
/// handler's, applied AFTER the code has been checked, so a stranger with a
/// handle learns nothing about the policy and spends nothing on the hasher.
/// So is whether the password and the name are required at all (OI-215): the
/// Registration switches decide, read by the handler after the code.
/// </summary>
public class CompleteRegistrationCommandValidator : AbstractValidator<CompleteRegistrationCommand>
{
    public CompleteRegistrationCommandValidator()
    {
        RuleFor(x => x.Otp).IsValidEmailOtp();
        RuleFor(x => x.Password).IsOptionalPassword();
        RuleFor(x => x.FirstName).IsValidOptionalFirstName();
        RuleFor(x => x.LastName).IsValidOptionalLastName();
        RuleFor(x => x.TimeZone).IsValidTimeZone(UserErrors.TimeZoneInvalid);
    }
}
