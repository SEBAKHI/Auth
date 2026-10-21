using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.RetireHmacKey;

/// <summary>
/// Handler for retiring a previous HMAC key. Refused, before the confirmation is spent,
/// while live webhook keys stored under it still validate within its grace: retiring it
/// would stop every one of them at once (KR3).
/// </summary>
public class RetireHmacKeyCommandHandler : IRequestHandler<RetireHmacKeyCommand, ErrorOr<HmacKeyringDto>>
{
    private readonly HmacKeyringChangeService _keyring;
    private readonly IWebhookKeyRepository _webhookKeyRepository;

    public RetireHmacKeyCommandHandler(HmacKeyringChangeService keyring, IWebhookKeyRepository webhookKeyRepository)
    {
        _keyring = keyring;
        _webhookKeyRepository = webhookKeyRepository;
    }

    public Task<ErrorOr<HmacKeyringDto>> Handle(RetireHmacKeyCommand request, CancellationToken cancellationToken) =>
        _keyring.ChangeAsync(
            new KeyringChangeRequest(
                KeyringAction.Retire,
                KeyringStepUp.OperationOf(KeyringAction.Retire),
                request.ChallengeId,
                request.RequestedBy,
                KeyId: request.KeyId),
            (ring, _) => ring.Retire(request.KeyId),
            precheck: (ring, now, token) => EnsureNoWebhookKeyNeedsItAsync(ring, request.KeyId, now, token),
            cancellationToken);

    private async Task<ErrorOr<Success>> EnsureNoWebhookKeyNeedsItAsync(
        HmacKeyring ring, int keyId, DateTime now, CancellationToken cancellationToken)
    {
        // Past its grace nothing stored under the key validates any more; within it,
        // every live webhook key stored under it still does.
        if (ring.AcceptedUntil(ring.Find(keyId)!, CredentialPurpose.WebhookKey) is not { } graceEnds || graceEnds < now)
        {
            return Result.Success;
        }

        var stored = (await _webhookKeyRepository.CountLiveByHmacKeyAsync(cancellationToken)).GetValueOrDefault(keyId);
        return stored > 0
            ? SecretErrors.KeyringKeyReferenced(keyId, stored, graceEnds)
            : Result.Success;
    }
}
