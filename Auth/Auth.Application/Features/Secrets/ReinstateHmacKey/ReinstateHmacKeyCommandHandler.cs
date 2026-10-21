using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.ReinstateHmacKey;

/// <summary>Handler for reinstating a previous HMAC key.</summary>
public class ReinstateHmacKeyCommandHandler : IRequestHandler<ReinstateHmacKeyCommand, ErrorOr<HmacKeyringDto>>
{
    private readonly HmacKeyringChangeService _keyring;

    public ReinstateHmacKeyCommandHandler(HmacKeyringChangeService keyring)
    {
        _keyring = keyring;
    }

    public Task<ErrorOr<HmacKeyringDto>> Handle(ReinstateHmacKeyCommand request, CancellationToken cancellationToken) =>
        _keyring.ChangeAsync(
            new KeyringChangeRequest(
                KeyringAction.Reinstate,
                KeyringStepUp.OperationOf(KeyringAction.Reinstate),
                request.ChallengeId,
                request.RequestedBy,
                KeyId: request.KeyId),
            (ring, change) => ring.Reinstate(request.KeyId, change.NowUtc, change.DemotedKeyVerifyUntilUtc),
            precheck: null,
            cancellationToken);
}
