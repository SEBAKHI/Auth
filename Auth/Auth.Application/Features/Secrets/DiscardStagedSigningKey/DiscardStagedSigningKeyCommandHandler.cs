using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.DiscardStagedSigningKey;

/// <summary>
/// Handler for discarding the staged signing key. The file alone changes (PB1): a process that
/// loaded the key keeps publishing and accepting it until its next restart, which is what the
/// answer and the console say.
/// </summary>
public class DiscardStagedSigningKeyCommandHandler
    : IRequestHandler<DiscardStagedSigningKeyCommand, ErrorOr<SigningKeyringDto>>
{
    private readonly RsaKeyringChangeService _keyring;

    public DiscardStagedSigningKeyCommandHandler(RsaKeyringChangeService keyring)
    {
        _keyring = keyring;
    }

    public Task<ErrorOr<SigningKeyringDto>> Handle(DiscardStagedSigningKeyCommand request, CancellationToken cancellationToken) =>
        _keyring.ChangeAsync(
            new SigningKeyChangeRequest(
                KeyringAction.DiscardNext,
                KeyringStepUp.SigningOperationOf(KeyringAction.DiscardNext),
                request.ChallengeId,
                request.RequestedBy),
            newKey: null,
            (ring, change) => ring.DiscardNext(change.NowUtc),
            applyInProcess: null,
            cancellationToken);
}
