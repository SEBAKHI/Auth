using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.DiscardStagedHmacKey;

/// <summary>Handler for removing the staged HMAC key.</summary>
public class DiscardStagedHmacKeyCommandHandler : IRequestHandler<DiscardStagedHmacKeyCommand, ErrorOr<HmacKeyringDto>>
{
    private readonly HmacKeyringChangeService _keyring;

    public DiscardStagedHmacKeyCommandHandler(HmacKeyringChangeService keyring)
    {
        _keyring = keyring;
    }

    public Task<ErrorOr<HmacKeyringDto>> Handle(DiscardStagedHmacKeyCommand request, CancellationToken cancellationToken) =>
        _keyring.ChangeAsync(
            new KeyringChangeRequest(
                KeyringAction.DiscardNext,
                KeyringStepUp.OperationOf(KeyringAction.DiscardNext),
                request.ChallengeId,
                request.RequestedBy),
            (ring, _) => ring.DiscardNext(),
            precheck: null,
            cancellationToken);
}
