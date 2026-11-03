using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.RetireSigningKey;

/// <summary>Validates the shape of a retire request.</summary>
public class RetireSigningKeyCommandValidator : AbstractValidator<RetireSigningKeyCommand>
{
    public RetireSigningKeyCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty().WithErrorCode(SecretErrors.ChallengeIdRequired.Code);

        RuleFor(x => x.Kid)
            .NotEmpty().WithErrorCode(SecretErrors.OperationInvalid.Code);
    }
}
