using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Authentication.StartRegistration;

public class StartRegistrationCommandValidator : AbstractValidator<StartRegistrationCommand>
{
    public StartRegistrationCommandValidator()
    {
        RuleFor(x => x.Email).IsValidEmail();
        RuleFor(x => x.PreferredLanguage).IsValidPreferredLanguage();
    }
}
