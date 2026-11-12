using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.TurnstileSecret;

/// <summary>
/// Validates <see cref="SetTurnstileSecretCommand"/>: length only, as for the Google client
/// secret. The format is Cloudflare's; a wrong value shows in the posture's reachability line
/// after the restart (Cloudflare refuses the secret).
/// </summary>
public class SetTurnstileSecretCommandValidator : AbstractValidator<SetTurnstileSecretCommand>
{
    public SetTurnstileSecretCommandValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty().WithErrorCode(SecretErrors.ValueRequired.Code)
            .MaximumLength(512).WithErrorCode(SecretErrors.ValueTooLong.Code);
    }
}
