using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.StageSigningKey;

/// <summary>
/// Handler for staging a signing key: a generated one, or the administrator's (StageImport),
/// whose confirmation is bound to that exact key. The file alone changes: this process loads
/// the staged key at its next start (PB1), which is why the console asks for that restart.
/// </summary>
public class StageSigningKeyCommandHandler : IRequestHandler<StageSigningKeyCommand, ErrorOr<SigningKeyringDto>>
{
    private readonly RsaKeyringChangeService _keyring;
    private readonly IKeyMaterialGenerator _keyMaterial;

    public StageSigningKeyCommandHandler(RsaKeyringChangeService keyring, IKeyMaterialGenerator keyMaterial)
    {
        _keyring = keyring;
        _keyMaterial = keyMaterial;
    }

    public async Task<ErrorOr<SigningKeyringDto>> Handle(StageSigningKeyCommand request, CancellationToken cancellationToken)
    {
        if (request.Value is { } supplied)
        {
            var key = RsaKeyringFile.FromPrivateKeyPem(supplied);
            if (key.IsError)
            {
                return key.Errors;
            }

            return await _keyring.ChangeAsync(
                new SigningKeyChangeRequest(
                    KeyringAction.StageImport,
                    KeyringStepUp.SigningOperationOf(KeyringAction.StageImport),
                    request.ChallengeId,
                    request.RequestedBy,
                    Value: supplied),
                key.Value,
                (ring, change) => ring.StageImport(change.NewKey!, change.NowUtc),
                applyInProcess: null,
                cancellationToken);
        }

        var generated = RsaKeyringFile.FromPrivateKeyPem(_keyMaterial.GenerateRsaKeyPair().PrivateKeyPem);
        if (generated.IsError)
        {
            return generated.Errors;
        }

        return await _keyring.ChangeAsync(
            new SigningKeyChangeRequest(
                KeyringAction.Stage,
                KeyringStepUp.SigningOperationOf(KeyringAction.Stage),
                request.ChallengeId,
                request.RequestedBy),
            generated.Value,
            (ring, change) => ring.Stage(change.NewKey!, change.NowUtc),
            applyInProcess: null,
            cancellationToken);
    }
}
