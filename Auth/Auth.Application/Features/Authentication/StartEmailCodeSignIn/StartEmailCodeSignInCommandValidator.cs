using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.StartEmailCodeSignIn;

public class StartEmailCodeSignInCommandValidator : AbstractValidator<StartEmailCodeSignInCommand>
{
    public StartEmailCodeSignInCommandValidator()
    {
        RuleFor(x => x.Email).IsValidEmail();
        RuleFor(x => x.PreferredLanguage).IsValidPreferredLanguage();
    }
}
