using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Passkeys.PasskeyLookup;

/// <summary>
/// The address, as the code sign-in's start checks it: a malformed address gets the same 400
/// there and here.
/// </summary>
public class PasskeyLookupQueryValidator : AbstractValidator<PasskeyLookupQuery>
{
    public PasskeyLookupQueryValidator()
    {
        RuleFor(x => x.Email).IsValidEmail();
    }
}
