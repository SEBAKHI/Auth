using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(OrganizationErrors.CodeRequired.Code)
            .MaximumLength(50).WithErrorCode(OrganizationErrors.CodeTooLong.Code)
            .Matches("^[a-zA-Z0-9_-]+$").WithErrorCode(OrganizationErrors.CodeInvalidFormat.Code);

        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(OrganizationErrors.NameRequired.Code)
            .MaximumLength(200).WithErrorCode(OrganizationErrors.NameTooLong.Code);

        RuleFor(x => x.ContactEmail).IsValidContactEmail();

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithErrorCode(OrganizationErrors.DescriptionTooLong.Code)
            .When(x => x.Description is not null);

        RuleFor(x => x.Website)
            .MaximumLength(500).WithErrorCode(OrganizationErrors.WebsiteTooLong.Code)
            .When(x => x.Website is not null);
    }
}
