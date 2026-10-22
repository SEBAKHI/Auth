using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.StageHmacKey;

/// <summary>
/// Handler for staging an HMAC key: a generated one, or the administrator's
/// (StageImport), whose confirmation is bound to that exact material.
/// </summary>
public class StageHmacKeyCommandHandler : IRequestHandler<StageHmacKeyCommand, ErrorOr<HmacKeyringDto>>
{
    private readonly HmacKeyringChangeService _keyring;
    private readonly IKeyMaterialGenerator _keyMaterial;

    public StageHmacKeyCommandHandler(HmacKeyringChangeService keyring, IKeyMaterialGenerator keyMaterial)
    {
        _keyring = keyring;
        _keyMaterial = keyMaterial;
    }

    public async Task<ErrorOr<HmacKeyringDto>> Handle(StageHmacKeyCommand request, CancellationToken cancellationToken)
    {
        if (request.Value is { } supplied)
        {
            var valid = SecretKeyMaterial.ValidateHmacKey(supplied);
            if (valid.IsError)
            {
                return valid.Errors;
            }

            return await _keyring.ChangeAsync(
                new KeyringChangeRequest(
                    KeyringAction.StageImport,
                    KeyringStepUp.OperationOf(KeyringAction.StageImport),
                    request.ChallengeId,
                    request.RequestedBy,
                    Value: supplied,
                    Material: supplied),
                (ring, change) => ring.StageImport(change.Material!, change.NowUtc),
                precheck: null,
                cancellationToken);
        }

        return await _keyring.ChangeAsync(
            new KeyringChangeRequest(
                KeyringAction.Stage,
                KeyringStepUp.OperationOf(KeyringAction.Stage),
                request.ChallengeId,
                request.RequestedBy,
                Material: _keyMaterial.GenerateHmacKeyBase64()),
            (ring, change) => ring.Stage(change.Material!, change.NowUtc),
            precheck: null,
            cancellationToken);
    }
}
