using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Users.GetUsers;

/// <summary>
/// Validates the GetUsersQuery input fields.
/// </summary>
public class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsValidPageNumber();
        RuleFor(x => x.PageSize).IsValidPageSize();
        RuleFor(x => x.SearchTerm)
            .MaximumLength(200).WithErrorCode(SearchErrors.TermTooLong.Code)
            .When(x => x.SearchTerm is not null);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.Users.Allowed);
    }
}
