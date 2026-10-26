using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Users.UpdateProfile;

/// <summary>
/// Validates the UpdateProfileCommand input fields.
/// </summary>
public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        // An empty name is allowed (OI-215): an account may have none, and must be
        // able to save its profile; only the length is bounded.
        RuleFor(x => x.FirstName).IsValidOptionalFirstName().When(x => x.FirstName is not null);
        RuleFor(x => x.LastName).IsValidOptionalLastName().When(x => x.LastName is not null);
        RuleFor(x => x.PhoneNumber).IsValidPhoneNumber().When(x => x.PhoneNumber is not null);
        RuleFor(x => x.PreferredLanguage).IsValidPreferredLanguage().When(x => x.PreferredLanguage is not null);
        RuleFor(x => x.TimeZone).IsValidTimeZone(UserErrors.TimeZoneInvalid).When(x => x.TimeZone is not null);
        RuleFor(x => x.Theme).IsValidTheme().When(x => x.Theme is not null);
    }
}
