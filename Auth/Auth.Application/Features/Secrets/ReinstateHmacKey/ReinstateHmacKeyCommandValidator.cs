using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.ReinstateHmacKey;

/// <summary>Validates the shape of a reinstate request.</summary>
public class ReinstateHmacKeyCommandValidator : AbstractValidator<ReinstateHmacKeyCommand>
{
    public ReinstateHmacKeyCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);
    }
}
