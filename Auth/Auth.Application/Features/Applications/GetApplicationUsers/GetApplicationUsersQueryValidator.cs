using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Applications.GetApplicationUsers;

/// <summary>
/// Validates the GetApplicationUsersQuery input fields.
/// </summary>
public class GetApplicationUsersQueryValidator : AbstractValidator<GetApplicationUsersQuery>
{
    public GetApplicationUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsValidPageNumber();
        RuleFor(x => x.PageSize).IsValidPageSize();
        RuleFor(x => x.SearchTerm)
            .MaximumLength(200).WithMessage("Validation.SearchTerm.MaxLength")
            .When(x => x.SearchTerm is not null);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.ApplicationUsers.Allowed);
    }
}
