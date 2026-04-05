using FluentValidation;

namespace Auth.Application.Features.Organizations.CreateOrganization;

/// <summary>
/// Validates the CreateOrganizationCommand input fields.
/// </summary>
public class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Validation.OrganizationCode.Required")
            .MaximumLength(50).WithMessage("Validation.OrganizationCode.MaxLength")
            .Matches("^[a-zA-Z0-9_-]+$").WithMessage("Validation.OrganizationCode.InvalidFormat");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Validation.OrganizationName.Required")
            .MaximumLength(200).WithMessage("Validation.OrganizationName.MaxLength");

        RuleFor(x => x.ContactEmail)
            .NotEmpty().WithMessage("Validation.ContactEmail.Required")
            .EmailAddress().WithMessage("Validation.ContactEmail.InvalidFormat")
            .MaximumLength(256).WithMessage("Validation.ContactEmail.MaxLength");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Validation.Description.MaxLength1000")
            .When(x => x.Description is not null);

        RuleFor(x => x.Website)
            .MaximumLength(500).WithMessage("Validation.WebsiteUrl.MaxLength")
            .When(x => x.Website is not null);
    }
}
