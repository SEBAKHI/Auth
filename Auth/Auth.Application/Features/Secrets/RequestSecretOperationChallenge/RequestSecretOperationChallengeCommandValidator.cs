using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
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
            .IsInEnum().WithErrorCode(SecretErrors.OperationInvalid.Code);

        // The import operations bind the confirmation to the material being
        // imported, so it has to be present at confirmation time, not later.
        RuleFor(x => x.Value)
            .NotEmpty().WithErrorCode(SecretErrors.OperationValueRequired.Code)
            .When(x => x.Operation is SecretOperation.ImportRsaKey
                or SecretOperation.ImportHmacKey
                or SecretOperation.ImportGatewayToken);

        // A keyring action is one of the known ones, confirmed by its own operation, and
        // a reinstate or a retire names the key it applies to.
        RuleFor(x => x.Action)
            .Must((command, action) => action is null || KeyringStepUp.Confirms(action.Value, command.Operation))
            .WithErrorCode(SecretErrors.OperationInvalid.Code);

        RuleFor(x => x.KeyId)
            .NotNull().WithErrorCode(SecretErrors.OperationInvalid.Code)
            .When(x => x.Action is KeyringAction.Reinstate or KeyringAction.Retire);
    }
}
