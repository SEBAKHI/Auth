using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Organizations.UpdateOrganization;

/// <summary>
/// Validates the UpdateOrganizationCommand input fields.
/// </summary>
public class UpdateOrganizationCommandValidator : AbstractValidator<UpdateOrganizationCommand>
{
    public UpdateOrganizationCommandValidator()
    {
        // The same rules, and so the same codes, as CreateOrganizationCommandValidator:
        // one field keeps one rule whichever endpoint writes it (ADR 0001). Update
        // used to cap the description at 500 while create and the column allow 1000,
        // and the URLs at 2048 against NVARCHAR(500) columns.
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(OrganizationErrors.NameRequired.Code)
            .MaximumLength(200).WithErrorCode(OrganizationErrors.NameTooLong.Code);

        RuleFor(x => x.ContactEmail).IsValidContactEmail();

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithErrorCode(OrganizationErrors.DescriptionTooLong.Code)
            .When(x => x.Description is not null);

        RuleFor(x => x.LogoUrl)
            .MaximumLength(500).WithErrorCode(OrganizationErrors.LogoUrlTooLong.Code)
            .When(x => x.LogoUrl is not null);

        RuleFor(x => x.Website)
            .MaximumLength(500).WithErrorCode(OrganizationErrors.WebsiteTooLong.Code)
            .When(x => x.Website is not null);
    }
}
