using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.RetireHmacKey;

/// <summary>Validates the shape of a retire request.</summary>
public class RetireHmacKeyCommandValidator : AbstractValidator<RetireHmacKeyCommand>
{
    public RetireHmacKeyCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);
    }
}
