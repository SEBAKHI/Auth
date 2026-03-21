using Auth.Application.Validators.Rules;
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
            .MaximumLength(200).WithMessage("Search term must not exceed 200 characters.")
            .When(x => x.SearchTerm is not null);
    }
}
