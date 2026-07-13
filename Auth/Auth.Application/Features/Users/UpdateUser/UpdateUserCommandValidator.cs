using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Users.UpdateUser;

/// <summary>
/// Validates the UpdateUserCommand input fields.
/// </summary>
public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.FirstName).IsValidFirstName();
        RuleFor(x => x.LastName).IsValidLastName();
        RuleFor(x => x.DisplayName).IsValidDisplayName().When(x => x.DisplayName is not null);
        RuleFor(x => x.PhoneNumber).IsValidPhoneNumber().When(x => x.PhoneNumber is not null);
        RuleFor(x => x.PreferredLanguage).IsValidPreferredLanguage().When(x => x.PreferredLanguage is not null);
        RuleFor(x => x.TimeZone).IsValidTimeZone().When(x => x.TimeZone is not null);
        RuleFor(x => x.Theme).IsValidTheme().When(x => x.Theme is not null);
    }
}
