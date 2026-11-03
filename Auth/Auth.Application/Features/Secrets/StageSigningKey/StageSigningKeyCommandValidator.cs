using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.StageSigningKey;

/// <summary>
/// Validates the shape of a stage request. Whether supplied material is a usable key is
/// decided in the handler, with the check every RSA import shares.
/// </summary>
public class StageSigningKeyCommandValidator : AbstractValidator<StageSigningKeyCommand>
{
    public StageSigningKeyCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);
    }
}
