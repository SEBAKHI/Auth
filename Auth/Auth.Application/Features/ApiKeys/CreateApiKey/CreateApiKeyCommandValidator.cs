using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(EnvironmentErrors.Required.Code)
            .MaximumLength(50).WithErrorCode(EnvironmentErrors.TooLong.Code);
        RuleFor(x => x.RateLimitPerMinute)
            .GreaterThan(0).WithErrorCode(ApiKeyErrors.RateLimitPerMinuteNotPositive.Code);
        RuleFor(x => x.RateLimitPerDay)
            .GreaterThan(0).WithErrorCode(ApiKeyErrors.RateLimitPerDayNotPositive.Code);
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow).WithErrorCode(ExpiryErrors.NotInFuture.Code)
            .When(x => x.ExpiresAt.HasValue);
    }
}
