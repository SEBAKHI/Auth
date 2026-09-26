using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Applications.GetApplications;

/// <summary>
/// Validates the GetApplicationsQuery input fields.
/// </summary>
public class GetApplicationsQueryValidator : AbstractValidator<GetApplicationsQuery>
{
    public GetApplicationsQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsValidPageNumber();
        RuleFor(x => x.PageSize).IsValidPageSize();
        RuleFor(x => x.Search)
            .MaximumLength(200).WithErrorCode(SearchErrors.TermTooLong.Code)
            .When(x => x.Search is not null);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.Applications.Allowed);
    }
}
