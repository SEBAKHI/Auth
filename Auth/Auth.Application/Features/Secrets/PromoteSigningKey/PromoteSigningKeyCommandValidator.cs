using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.PromoteSigningKey;

/// <summary>Validates the shape of a promote request.</summary>
public class PromoteSigningKeyCommandValidator : AbstractValidator<PromoteSigningKeyCommand>
{
    public PromoteSigningKeyCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);
    }
}
