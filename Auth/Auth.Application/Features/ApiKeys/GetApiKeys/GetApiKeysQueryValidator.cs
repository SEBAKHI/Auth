using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.ApiKeys.GetApiKeys;

/// <summary>
/// Validates the GetApiKeysQuery input fields.
/// </summary>
public class GetApiKeysQueryValidator : AbstractValidator<GetApiKeysQuery>
{
    public GetApiKeysQueryValidator()
    {
        // Omitting the filter is legitimate (every application); supplying an empty Guid
        // is not — it is a caller that meant to narrow and lost the value on the way.
        RuleFor(x => x.ApplicationId)
            .NotEqual(Guid.Empty).WithMessage("Validation.ApplicationId.Required")
            .When(x => x.ApplicationId.HasValue);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.ApiKeys.Allowed);
    }
}
