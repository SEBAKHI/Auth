using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Validates the GetExternalProvidersQuery input fields.
/// </summary>
public class GetExternalProvidersQueryValidator : AbstractValidator<GetExternalProvidersQuery>
{
    public GetExternalProvidersQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.ExternalProviders.Allowed);
    }
}
