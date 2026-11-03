using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateRsaKey;

/// <summary>
/// Handler for regenerating the signing key: since the signing keyring (S12), an alias of a
/// stage of a generated key and its emergency promote, in one change (PB3). Its confirmation
/// carries today's digest (none), so a console cached before the keyring shipped keeps working
/// until SUNSET(S12) removes the alias.
/// </summary>
/// <remarks>
/// Today's meaning is kept: the new key signs from the next start of the API, and every token
/// the old key signed is refused from then on, so users re-authenticate. A rotation that signs
/// nobody out is keyring/rsa/stage, a restart, then keyring/rsa/promote 24 hours later.
/// </remarks>
public class GenerateRsaKeyCommandHandler : IRequestHandler<GenerateRsaKeyCommand, ErrorOr<string>>
{
    private readonly RsaKeyringChangeService _keyring;
    private readonly IKeyMaterialGenerator _keyMaterial;

    public GenerateRsaKeyCommandHandler(RsaKeyringChangeService keyring, IKeyMaterialGenerator keyMaterial)
    {
        _keyring = keyring;
        _keyMaterial = keyMaterial;
    }

    public async Task<ErrorOr<string>> Handle(
        GenerateRsaKeyCommand request,
        CancellationToken cancellationToken)
    {
        var generated = RsaKeyringFile.FromPrivateKeyPem(_keyMaterial.GenerateRsaKeyPair().PrivateKeyPem);
        if (generated.IsError)
        {
            return generated.Errors;
        }

        var changed = await _keyring.ChangeAsync(
            new SigningKeyChangeRequest(
                KeyringAction.PromoteEmergency,
                SecretOperation.GenerateRsaKey,
                request.ChallengeId,
                request.RequestedBy,
                AliasDigest: true),
            generated.Value,
            (ring, change) => ring.ReplaceInEmergency(change.NewKey!, change.NowUtc),
            applyInProcess: null,
            cancellationToken);

        return changed.IsError ? changed.Errors : generated.Value.PublicKeyPem!;
    }
}
