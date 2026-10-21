using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateHmacKey;

/// <summary>
/// Handler for regenerating the HMAC key: since the keyring (S11), an alias of an
/// emergency promote of a generated key. Its confirmation carries today's digest
/// (none), so a console cached before the keyring shipped keeps working until
/// SUNSET(S11) removes the alias.
/// </summary>
/// <remarks>
/// Today's meaning is kept: everything computed under the old key stops working now.
/// One thing is better than before: webhook keys keep validating for 90 days and are
/// re-fingerprinted on their next use, instead of all failing at once.
/// </remarks>
public class GenerateHmacKeyCommandHandler : IRequestHandler<GenerateHmacKeyCommand, ErrorOr<Success>>
{
    private readonly HmacKeyringChangeService _keyring;
    private readonly IKeyMaterialGenerator _keyMaterial;

    public GenerateHmacKeyCommandHandler(HmacKeyringChangeService keyring, IKeyMaterialGenerator keyMaterial)
    {
        _keyring = keyring;
        _keyMaterial = keyMaterial;
    }

    public async Task<ErrorOr<Success>> Handle(
        GenerateHmacKeyCommand request,
        CancellationToken cancellationToken)
    {
        var changed = await _keyring.ChangeAsync(
            new KeyringChangeRequest(
                KeyringAction.PromoteEmergency,
                SecretOperation.GenerateHmacKey,
                request.ChallengeId,
                request.RequestedBy,
                Material: _keyMaterial.GenerateHmacKeyBase64(),
                AliasDigest: true),
            (ring, change) => ring.PromoteEmergency(change.Material!, change.NowUtc),
            precheck: null,
            cancellationToken);

        return changed.IsError ? changed.Errors : Result.Success;
    }
}
