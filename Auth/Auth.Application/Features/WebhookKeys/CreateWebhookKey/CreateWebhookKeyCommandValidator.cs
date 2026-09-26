using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.WebhookKeys.CreateWebhookKey;

/// <summary>
/// Validates the CreateWebhookKeyCommand input fields.
/// </summary>
public class CreateWebhookKeyCommandValidator : AbstractValidator<CreateWebhookKeyCommand>
{
    public CreateWebhookKeyCommandValidator()
    {
        RuleFor(x => x.Name).IsValidName();
        RuleFor(x => x.Description).IsValidDescription().When(x => x.Description is not null);
        // NVARCHAR(2000): a longer URL passed the old 2048 ceiling and then failed the INSERT.
        RuleFor(x => x.TargetUrl)
            .MaximumLength(2000).WithErrorCode(WebhookKeyErrors.TargetUrlTooLong.Code);
        RuleFor(x => x.Environment)
            .NotEmpty().WithErrorCode(EnvironmentErrors.Required.Code)
            .MaximumLength(50).WithErrorCode(EnvironmentErrors.TooLong.Code);
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow).WithErrorCode(ExpiryErrors.NotInFuture.Code)
            .When(x => x.ExpiresAt.HasValue);
    }
}
