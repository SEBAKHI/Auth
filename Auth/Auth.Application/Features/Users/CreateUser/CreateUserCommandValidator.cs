using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Users.CreateUser;

/// <summary>
/// Validates the CreateUserCommand input fields.
/// </summary>
public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email).IsValidEmail();
        RuleFor(x => x.Password).IsRequiredPassword();
        RuleFor(x => x.FirstName).IsValidFirstName();
        RuleFor(x => x.LastName).IsValidLastName();
        RuleFor(x => x.PhoneNumber).IsValidPhoneNumber().When(x => x.PhoneNumber is not null);
        RuleFor(x => x.PreferredLanguage).IsValidPreferredLanguage().When(x => x.PreferredLanguage is not null);
        RuleFor(x => x.TimeZone).IsValidTimeZone().When(x => x.TimeZone is not null);
        RuleFor(x => x.Theme).IsValidTheme().When(x => x.Theme is not null);
    }
}
