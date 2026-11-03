using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.RetireSigningKey;

/// <summary>
/// Handler for retiring a signing key. A previous key still in its window stops being accepted
/// now, in this process too: dropping a key needs nothing loaded from the file.
/// </summary>
public class RetireSigningKeyCommandHandler : IRequestHandler<RetireSigningKeyCommand, ErrorOr<SigningKeyringDto>>
{
    private readonly RsaKeyringChangeService _keyring;

    public RetireSigningKeyCommandHandler(RsaKeyringChangeService keyring)
    {
        _keyring = keyring;
    }

    public Task<ErrorOr<SigningKeyringDto>> Handle(RetireSigningKeyCommand request, CancellationToken cancellationToken) =>
        _keyring.ChangeAsync(
            new SigningKeyChangeRequest(
                KeyringAction.Retire,
                KeyringStepUp.SigningOperationOf(KeyringAction.Retire),
                request.ChallengeId,
                request.RequestedBy,
                Kid: request.Kid),
            newKey: null,
            (ring, change) => ring.Retire(request.Kid, change.NowUtc),
            (ring, change) => ring.Retire(request.Kid, change.NowUtc),
            cancellationToken);
}
