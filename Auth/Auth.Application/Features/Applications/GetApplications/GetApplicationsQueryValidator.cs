using Auth.Application.Validators.Rules;
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
            .MaximumLength(200).WithMessage("Search term must not exceed 200 characters.")
            .When(x => x.Search is not null);
    }
}
