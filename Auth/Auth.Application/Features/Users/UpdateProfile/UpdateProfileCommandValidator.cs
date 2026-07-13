using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Users.UpdateProfile;

/// <summary>
/// Validates the UpdateProfileCommand input fields.
/// </summary>
public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.FirstName!).IsValidFirstName().When(x => x.FirstName is not null);
        RuleFor(x => x.LastName!).IsValidLastName().When(x => x.LastName is not null);
        RuleFor(x => x.DisplayName).IsValidDisplayName().When(x => x.DisplayName is not null);
        RuleFor(x => x.PhoneNumber).IsValidPhoneNumber().When(x => x.PhoneNumber is not null);
        RuleFor(x => x.PreferredLanguage).IsValidPreferredLanguage().When(x => x.PreferredLanguage is not null);
        RuleFor(x => x.TimeZone).IsValidTimeZone().When(x => x.TimeZone is not null);
        RuleFor(x => x.Theme).IsValidTheme().When(x => x.Theme is not null);
    }
}
