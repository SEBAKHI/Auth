using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.StageHmacKey;

/// <summary>
/// Validates the shape of a stage request. Whether supplied material is a usable key
/// is decided in the handler, with the check every HMAC import shares.
/// </summary>
public class StageHmacKeyCommandValidator : AbstractValidator<StageHmacKeyCommand>
{
    public StageHmacKeyCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);
    }
}
