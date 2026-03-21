using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Applications.UpdateApplication;

/// <summary>
/// Validates the UpdateApplicationCommand input fields.
/// </summary>
public class UpdateApplicationCommandValidator : AbstractValidator<UpdateApplicationCommand>
{
    public UpdateApplicationCommandValidator()
    {
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
        RuleFor(x => x.BaseUrl).IsValidUrl().When(x => x.BaseUrl is not null);
        RuleFor(x => x.LogoUrl).IsValidUrl().When(x => x.LogoUrl is not null);
        RuleFor(x => x.ContactEmail!).IsValidEmail().When(x => x.ContactEmail is not null);
        RuleFor(x => x.SessionTimeoutMinutes).GreaterThan(0);
        RuleFor(x => x.MaxConcurrentSessions).GreaterThan(0);
    }
}
