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
            .NotEmpty().WithMessage("Organization code is required.")
            .MaximumLength(50).WithMessage("Organization code must not exceed 50 characters.")
            .Matches("^[a-zA-Z0-9_-]+$").WithMessage("Organization code must contain only letters, numbers, hyphens, and underscores.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organization name is required.")
            .MaximumLength(200).WithMessage("Organization name must not exceed 200 characters.");

        RuleFor(x => x.ContactEmail)
            .NotEmpty().WithMessage("Contact email is required.")
            .EmailAddress().WithMessage("A valid contact email address is required.")
            .MaximumLength(256).WithMessage("Contact email must not exceed 256 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.")
            .When(x => x.Description is not null);

        RuleFor(x => x.Website)
            .MaximumLength(500).WithMessage("Website URL must not exceed 500 characters.")
            .When(x => x.Website is not null);
    }
}
