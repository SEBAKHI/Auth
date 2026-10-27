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
        // length is bounded. An omitted one (null) is still refused, as before: this
        // is a full update, so a request that leaves the names out must not blank the
        // stored ones; an empty string is how a name is cleared.
        RuleFor(x => x.FirstName)
            .NotNull().WithErrorCode(UserErrors.FirstNameRequired.Code)
            .IsValidOptionalFirstName();
        RuleFor(x => x.LastName)
            .NotNull().WithErrorCode(UserErrors.LastNameRequired.Code)
            .IsValidOptionalLastName();
        RuleFor(x => x.PhoneNumber).IsValidPhoneNumber().When(x => x.PhoneNumber is not null);
        RuleFor(x => x.PreferredLanguage).IsValidPreferredLanguage().When(x => x.PreferredLanguage is not null);
        RuleFor(x => x.TimeZone).IsValidTimeZone(UserErrors.TimeZoneInvalid).When(x => x.TimeZone is not null);
        RuleFor(x => x.Theme).IsValidTheme().When(x => x.Theme is not null);
    }
}
