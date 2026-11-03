using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.DiscardStagedSigningKey;

/// <summary>Validates the shape of a discard request.</summary>
public class DiscardStagedSigningKeyCommandValidator : AbstractValidator<DiscardStagedSigningKeyCommand>
{
    public DiscardStagedSigningKeyCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);
    }
}
