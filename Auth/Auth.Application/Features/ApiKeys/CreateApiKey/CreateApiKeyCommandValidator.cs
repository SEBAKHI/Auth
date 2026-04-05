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
            .NotEmpty().WithMessage("Validation.Environment.Required")
            .MaximumLength(50);
        RuleFor(x => x.RateLimitPerMinute)
            .GreaterThan(0).WithMessage("Validation.RateLimitPerMinute.GreaterThanZero");
        RuleFor(x => x.RateLimitPerDay)
            .GreaterThan(0).WithMessage("Validation.RateLimitPerDay.GreaterThanZero");
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow).WithMessage("Validation.ExpirationDate.Future")
            .When(x => x.ExpiresAt.HasValue);
    }
}
