using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.ImportHmacKey;

/// <summary>
/// Handler for importing a caller-supplied HMAC key: since the keyring (S11), an alias
/// of an emergency promote of that key. Validates the value (base64, at least 256 bits)
/// before anything is spent, and spends a confirmation bound to a digest of this exact
/// material, today's digest, so a console cached before the keyring keeps working until
/// SUNSET(S11) removes the alias.
/// </summary>
public class ImportHmacKeyCommandHandler : IRequestHandler<ImportHmacKeyCommand, ErrorOr<Success>>
{
    private readonly HmacKeyringChangeService _keyring;
    private readonly SecretManagementSettings _settings;

    public ImportHmacKeyCommandHandler(
        HmacKeyringChangeService keyring,
        IOptions<SecretManagementSettings> settings)
    {
        _keyring = keyring;
        _settings = settings.Value;
    }

    public async Task<ErrorOr<Success>> Handle(
        ImportHmacKeyCommand request,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.ImportNotSupportedInPlainText;
        }

        var material = SecretKeyMaterial.ValidateHmacKey(request.HmacKeyBase64);
        if (material.IsError)
        {
            return material.Errors;
        }

        var changed = await _keyring.ChangeAsync(
            new KeyringChangeRequest(
                KeyringAction.PromoteEmergency,
                SecretOperation.ImportHmacKey,
                request.ChallengeId,
                request.RequestedBy,
                Value: request.HmacKeyBase64,
                Material: request.HmacKeyBase64,
                AliasDigest: true),
            (ring, change) => ring.PromoteEmergency(change.Material!, change.NowUtc),
            precheck: null,
            cancellationToken);

        return changed.IsError ? changed.Errors : Result.Success;
    }
}
