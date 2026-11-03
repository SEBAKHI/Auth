using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.PromoteSigningKey;

/// <summary>
/// Handler for promoting the staged signing key. A scheduled promote needs the staged key
/// loaded in this process and published for 24 hours (KeyringStepUp checks both before a code
/// is emailed and before it is spent); the signer then moves to it here at once, so the old
/// key's window starts when it stops signing. An emergency skips both checks: it closes the
/// leaked key here at once when this process loaded the staged key, and otherwise at the next
/// restart, which the answer says (<see cref="SigningKeyringDto.ChangeAppliedInProcess"/>).
/// </summary>
public class PromoteSigningKeyCommandHandler : IRequestHandler<PromoteSigningKeyCommand, ErrorOr<SigningKeyringDto>>
{
    private readonly RsaKeyringChangeService _keyring;

    public PromoteSigningKeyCommandHandler(RsaKeyringChangeService keyring)
    {
        _keyring = keyring;
    }

    public Task<ErrorOr<SigningKeyringDto>> Handle(PromoteSigningKeyCommand request, CancellationToken cancellationToken)
    {
        if (request.Emergency)
        {
            return _keyring.ChangeAsync(
                new SigningKeyChangeRequest(
                    KeyringAction.PromoteEmergency,
                    KeyringStepUp.SigningOperationOf(KeyringAction.PromoteEmergency),
                    request.ChallengeId,
                    request.RequestedBy),
                newKey: null,
                (ring, change) => ring.PromoteEmergency(change.NowUtc),
                RsaKeyringChangeService.WithStagedKeyLoaded((ring, change) => ring.PromoteEmergency(change.NowUtc)),
                cancellationToken);
        }

        return _keyring.ChangeAsync(
            new SigningKeyChangeRequest(
                KeyringAction.Promote,
                KeyringStepUp.SigningOperationOf(KeyringAction.Promote),
                request.ChallengeId,
                request.RequestedBy),
            newKey: null,
            (ring, change) => ring.Promote(change.NowUtc, change.PreviousVerifyUntilUtc),
            RsaKeyringChangeService.WithStagedKeyLoaded((ring, change) => ring.Promote(change.NowUtc, change.PreviousVerifyUntilUtc)),
            cancellationToken);
    }
}
