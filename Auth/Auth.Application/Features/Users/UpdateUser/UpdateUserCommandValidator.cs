using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Users.UpdateUser;

/// <summary>
/// Validates the UpdateUserCommand input fields.
/// </summary>
public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        // An empty name is allowed (OI-215): an account may have none; only the
        // length is bounded.
        RuleFor(x => x.FirstName).IsValidOptionalFirstName();
        RuleFor(x => x.LastName).IsValidOptionalLastName();
        RuleFor(x => x.PhoneNumber).IsValidPhoneNumber().When(x => x.PhoneNumber is not null);
        RuleFor(x => x.PreferredLanguage).IsValidPreferredLanguage().When(x => x.PreferredLanguage is not null);
        RuleFor(x => x.TimeZone).IsValidTimeZone(UserErrors.TimeZoneInvalid).When(x => x.TimeZone is not null);
        RuleFor(x => x.Theme).IsValidTheme().When(x => x.Theme is not null);
    }
}
