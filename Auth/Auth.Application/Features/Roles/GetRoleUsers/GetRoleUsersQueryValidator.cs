using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Roles.GetRoleUsers;

/// <summary>
/// Validates the GetRoleUsersQuery input fields.
/// </summary>
public class GetRoleUsersQueryValidator : AbstractValidator<GetRoleUsersQuery>
{
    public GetRoleUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsValidPageNumber();
        RuleFor(x => x.PageSize).IsValidPageSize();
        RuleFor(x => x.SearchTerm)
            .MaximumLength(200).WithMessage("Validation.SearchTerm.MaxLength")
            .When(x => x.SearchTerm is not null);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.RoleUsers.Allowed);
    }
}
