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
            .NotEmpty().WithMessage("API key is required.")
            .Must(key => key.StartsWith("ak_"))
            .WithMessage("API key must start with a valid prefix (ak_).");
    }
}
