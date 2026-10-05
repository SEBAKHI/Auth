using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Organizations.OrganizationSetup;

/// <summary>
/// The organization name follows the rules of every other organization name.
/// Not checked when an existing organization is chosen.
/// </summary>
public class SetUpOrganizationCommandValidator : AbstractValidator<SetUpOrganizationCommand>
{
    public SetUpOrganizationCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(OrganizationErrors.NameRequired.Code)
            .MaximumLength(200).WithErrorCode(OrganizationErrors.NameTooLong.Code)
            .When(x => x.OrganizationId is null);
    }
}
