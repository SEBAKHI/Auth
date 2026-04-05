using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Organizations.GetOrganizationMembers;

/// <summary>
/// Validates the GetOrganizationMembersQuery input fields.
/// </summary>
public class GetOrganizationMembersQueryValidator : AbstractValidator<GetOrganizationMembersQuery>
{
    public GetOrganizationMembersQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsValidPageNumber();
        RuleFor(x => x.PageSize).IsValidPageSize();
        RuleFor(x => x.SearchTerm)
            .MaximumLength(200).WithMessage("Validation.SearchTerm.MaxLength")
            .When(x => x.SearchTerm is not null);
    }
}
