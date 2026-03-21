using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.ApiKeys.CreateApiKey;

/// <summary>
/// Validates the CreateApiKeyCommand input fields.
/// </summary>
public class CreateApiKeyCommandValidator : AbstractValidator<CreateApiKeyCommand>
{
    public CreateApiKeyCommandValidator()
    {
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
        RuleFor(x => x.Environment)
            .NotEmpty().WithMessage("Environment is required.")
            .MaximumLength(50);
        RuleFor(x => x.RateLimitPerMinute)
            .GreaterThan(0).WithMessage("Rate limit per minute must be greater than 0.");
        RuleFor(x => x.RateLimitPerDay)
            .GreaterThan(0).WithMessage("Rate limit per day must be greater than 0.");
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow).WithMessage("Expiration date must be in the future.")
            .When(x => x.ExpiresAt.HasValue);
    }
}
