using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
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
            .MaximumLength(200).WithErrorCode(SearchErrors.TermTooLong.Code)
            .When(x => x.SearchTerm is not null);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.ApplicationUsers.Allowed);
    }
}
