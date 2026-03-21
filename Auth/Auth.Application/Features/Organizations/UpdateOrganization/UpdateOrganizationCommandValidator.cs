using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Organizations.UpdateOrganization;

/// <summary>
/// Validates the UpdateOrganizationCommand input fields.
/// </summary>
public class UpdateOrganizationCommandValidator : AbstractValidator<UpdateOrganizationCommand>
{
    public UpdateOrganizationCommandValidator()
    {
        RuleFor(x => x.Name).IsValidName();

        RuleFor(x => x.ContactEmail).IsValidEmail();

        RuleFor(x => x.Description)
            .IsValidDescription()
            .When(x => x.Description is not null);

        RuleFor(x => x.LogoUrl)
            .IsValidUrl()
            .When(x => x.LogoUrl is not null);

        RuleFor(x => x.Website)
            .IsValidUrl()
            .When(x => x.Website is not null);
    }
}
