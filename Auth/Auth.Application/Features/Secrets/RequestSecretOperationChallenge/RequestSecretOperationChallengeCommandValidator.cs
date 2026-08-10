using Auth.Domain.Enums;
using FluentValidation;

namespace Auth.Application.Features.Secrets.RequestSecretOperationChallenge;

/// <summary>
/// Validates the shape of a confirmation request. Whether the key material is
/// cryptographically usable is decided in the handler, which shares that check
/// with the import handlers.
/// </summary>
public class RequestSecretOperationChallengeCommandValidator
    : AbstractValidator<RequestSecretOperationChallengeCommand>
{
    public RequestSecretOperationChallengeCommandValidator()
    {
        RuleFor(x => x.Operation)
            .IsInEnum().WithMessage("Validation.SecretOperation.Invalid");

        // The import operations bind the confirmation to the material being
        // imported, so it has to be present at confirmation time, not later.
        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Validation.SecretOperation.ValueRequired")
            .When(x => x.Operation is SecretOperation.ImportRsaKey
                or SecretOperation.ImportHmacKey
                or SecretOperation.ImportGatewayToken);
    }
}
