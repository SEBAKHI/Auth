using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.PromoteHmacKey;

/// <summary>
/// Handler for promoting an HMAC key. A scheduled promote needs the staged key loaded in
/// this process as the file stages it: a process that does not verify under a key yet
/// must not start issuing under it (KR3).
/// </summary>
public class PromoteHmacKeyCommandHandler : IRequestHandler<PromoteHmacKeyCommand, ErrorOr<HmacKeyringDto>>
{
    private readonly HmacKeyringChangeService _keyring;
    private readonly IKeyMaterialGenerator _keyMaterial;

    public PromoteHmacKeyCommandHandler(HmacKeyringChangeService keyring, IKeyMaterialGenerator keyMaterial)
    {
        _keyring = keyring;
        _keyMaterial = keyMaterial;
    }

    public Task<ErrorOr<HmacKeyringDto>> Handle(PromoteHmacKeyCommand request, CancellationToken cancellationToken)
    {
        if (request.Emergency)
        {
            return _keyring.ChangeAsync(
                new KeyringChangeRequest(
                    KeyringAction.PromoteEmergency,
                    KeyringStepUp.OperationOf(KeyringAction.PromoteEmergency),
                    request.ChallengeId,
                    request.RequestedBy,
                    Material: _keyMaterial.GenerateHmacKeyBase64()),
                (ring, change) => ring.PromoteEmergency(change.Material!, change.NowUtc),
                precheck: null,
                cancellationToken);
        }

        return _keyring.ChangeAsync(
            new KeyringChangeRequest(
                KeyringAction.Promote,
                KeyringStepUp.OperationOf(KeyringAction.Promote),
                request.ChallengeId,
                request.RequestedBy),
            // The staged key must be the one this process loaded: KeyringStepUp checks it
            // before a code is emailed and before it is spent.
            (ring, change) => ring.Promote(change.NowUtc, change.DemotedKeyVerifyUntilUtc),
            precheck: null,
            cancellationToken);
    }
}
