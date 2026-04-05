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
            .NotEmpty().WithMessage("Validation.ApiKey.Required")
            .Must(key => key.StartsWith("ak_"))
            .WithMessage("Validation.ApiKey.InvalidPrefix");
    }
}
