using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.MachineKeys.RevokeMachineKey;

/// <summary>
/// Handles <see cref="RevokeMachineKeyCommand"/>. A key of another application is answered as no
/// key at all: <c>MachineKey.NotFound</c>.
/// </summary>
public class RevokeMachineKeyCommandHandler : IRequestHandler<RevokeMachineKeyCommand, ErrorOr<Success>>
{
    private readonly IApplicationMachineKeyRepository _machineKeys;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<RevokeMachineKeyCommandHandler> _logger;

    public RevokeMachineKeyCommandHandler(
        IApplicationMachineKeyRepository machineKeys,
        IPublisher publisher,
        TimeProvider time,
        ILogger<RevokeMachineKeyCommandHandler> logger)
    {
        _machineKeys = machineKeys;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(RevokeMachineKeyCommand request, CancellationToken cancellationToken)
    {
        var key = await _machineKeys.GetAsync(request.ApplicationId, request.MachineKeyId, cancellationToken);
        if (key is null)
        {
            return MachineKeyErrors.NotFound;
        }

        var now = _time.GetUtcNow();
        var revoked = key.Revoke(request.RevokedBy, reason: null, now.UtcDateTime);
        if (revoked.IsError)
        {
            return revoked.Errors;
        }

        // Conditional on the stored key still being active: of two concurrent revocations one
        // writes, and the other is told the key was already revoked.
        if (!await _machineKeys.TryRevokeAsync(key, cancellationToken))
        {
            return MachineKeyErrors.AlreadyRevoked;
        }

        _logger.LogInformation(
            "Machine key {Kid} of application {ApplicationId} revoked by {RevokedBy}",
            key.Kid, key.ApplicationId, request.RevokedBy);

        await _publisher.Publish(
            new MachineKeyRevokedEvent(Guid.NewGuid(), key.Id, key.ApplicationId, key.Kid, request.RevokedBy, now),
            CancellationToken.None);

        return Result.Success;
    }
}
