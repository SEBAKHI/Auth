using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.ApiKeys.ValidateApiKey;

/// <summary>
/// Validates the ValidateApiKeyQuery input fields.
/// </summary>
public class ValidateApiKeyQueryValidator : AbstractValidator<ValidateApiKeyQuery>
{
    public ValidateApiKeyQueryValidator()
    {
        RuleFor(x => x.RawApiKey)
            .NotEmpty().WithErrorCode(ApiKeyErrors.Required.Code)
            .Must(key => key.StartsWith("ak_"))
            .WithErrorCode(ApiKeyErrors.InvalidPrefix.Code);
    }
}
