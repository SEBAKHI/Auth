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
            .NotEmpty().WithMessage("Validation.SecretOperation.ChallengeRequired");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Validation.OtpCode.Required")
            .Length(6).WithMessage("Validation.OtpCode.InvalidFormat");
    }
}
