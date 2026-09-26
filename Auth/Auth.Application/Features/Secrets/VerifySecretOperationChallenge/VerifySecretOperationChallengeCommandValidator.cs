using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.VerifySecretOperationChallenge;

/// <summary>
/// Validates the shape of a submitted confirmation code. Whether it is the
/// right code is decided by the challenge service, which answers every failure
/// shape identically.
/// </summary>
public class VerifySecretOperationChallengeCommandValidator
    : AbstractValidator<VerifySecretOperationChallengeCommand>
{
    public VerifySecretOperationChallengeCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);

        RuleFor(x => x.Code)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeCodeRequired.Code)
            .Length(6).WithErrorCode(SecretErrors.ChallengeCodeInvalidFormat.Code);
    }
}
