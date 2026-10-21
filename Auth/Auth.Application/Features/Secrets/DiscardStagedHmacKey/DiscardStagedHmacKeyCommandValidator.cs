using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.DiscardStagedHmacKey;

/// <summary>Validates the shape of a discard request.</summary>
public class DiscardStagedHmacKeyCommandValidator : AbstractValidator<DiscardStagedHmacKeyCommand>
{
    public DiscardStagedHmacKeyCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);
    }
}
