using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.ImportRsaKey;

/// <summary>
/// Handler for importing a caller-supplied signing key: since the signing keyring (S12), an
/// alias of a staged import of that key and its emergency promote, in one change (PB3). Its
/// confirmation is bound, as before, to a digest of this exact key material, so a console cached
/// before the keyring shipped keeps working until SUNSET(S12) removes the alias.
/// </summary>
/// <remarks>
/// Today's meaning is kept: the key signs from the next start of the API, and every token the old
/// key signed is refused from then on. A key the keyring holds or once held is refused: taken back,
/// a key that left the ring (it may have leaked) would sign again with nothing to say so.
/// </remarks>
public class ImportRsaKeyCommandHandler : IRequestHandler<ImportRsaKeyCommand, ErrorOr<string>>
{
    private readonly RsaKeyringChangeService _keyring;
    private readonly SecretManagementSettings _settings;

    public ImportRsaKeyCommandHandler(
        RsaKeyringChangeService keyring,
        IOptions<SecretManagementSettings> settings)
    {
        _keyring = keyring;
        _settings = settings.Value;
    }

    public async Task<ErrorOr<string>> Handle(
        ImportRsaKeyCommand request,
        CancellationToken cancellationToken)
    {
        // The answer a cached console has always had in PlainText mode.
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.ImportNotSupportedInPlainText;
        }

        var supplied = RsaKeyringFile.FromPrivateKeyPem(request.PrivateKeyPem);
        if (supplied.IsError)
        {
            return supplied.Errors;
        }

        var changed = await _keyring.ChangeAsync(
            new SigningKeyChangeRequest(
                KeyringAction.PromoteEmergency,
                SecretOperation.ImportRsaKey,
                request.ChallengeId,
                request.RequestedBy,
                Value: request.PrivateKeyPem,
                AliasDigest: true),
            supplied.Value,
            (ring, change) => ring.ReplaceInEmergency(change.NewKey!, change.NowUtc),
            applyInProcess: null,
            cancellationToken);

        return changed.IsError ? changed.Errors : supplied.Value.PublicKeyPem!;
    }
}
