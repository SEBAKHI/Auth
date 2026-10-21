using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.PromoteHmacKey;

/// <summary>Validates the shape of a promote request.</summary>
public class PromoteHmacKeyCommandValidator : AbstractValidator<PromoteHmacKeyCommand>
{
    public PromoteHmacKeyCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);
    }
}
