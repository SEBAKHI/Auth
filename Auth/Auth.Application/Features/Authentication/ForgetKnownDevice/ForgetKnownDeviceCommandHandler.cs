using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ForgetKnownDevice;

/// <summary>
/// Handler for the forget-device command.
///
/// Forgetting is deliberately more than deleting a row. The record on its own is
/// recognition state — dropping it alone would leave the browser signed in while
/// telling the user it had been removed, and would silently re-arm the
/// new-device email for a browser they still use. Ending the sessions is what
/// makes the label true.
/// </summary>
public class ForgetKnownDeviceCommandHandler : IRequestHandler<ForgetKnownDeviceCommand, ErrorOr<int>>
{
    private const string TerminationReason = "device_forgotten";

    private readonly IUserKnownDeviceRepository _deviceRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly ICredentialRevocationService _revocationService;
    private readonly ILogger<ForgetKnownDeviceCommandHandler> _logger;

    public ForgetKnownDeviceCommandHandler(
        IUserKnownDeviceRepository deviceRepository,
        IUserSessionRepository sessionRepository,
        ICredentialRevocationService revocationService,
        ILogger<ForgetKnownDeviceCommandHandler> logger)
    {
        _deviceRepository = deviceRepository;
        _sessionRepository = sessionRepository;
        _revocationService = revocationService;
        _logger = logger;
    }

    public async Task<ErrorOr<int>> Handle(
        ForgetKnownDeviceCommand request,
        CancellationToken cancellationToken)
    {
        // Scoped to the user in SQL, so another user's id reads as absent rather
        // than forbidden and the endpoint cannot confirm that an id is real.
        var device = await _deviceRepository.GetByIdAsync(
            request.UserId, request.DeviceId, cancellationToken);

        if (device is null)
        {
            return DeviceErrors.NotFound;
        }

        if (request.CurrentSessionId.HasValue)
        {
            var sessions = await _sessionRepository.GetActiveByDeviceHashAsync(
                request.UserId, device.DeviceHash, cancellationToken);

            if (sessions.Any(s => s.Id == request.CurrentSessionId.Value))
            {
                return DeviceErrors.CannotForgetCurrent;
            }
        }

        // Sessions first. If the delete succeeded and this failed, the user would
        // be told the browser was removed while it stayed signed in — the one
        // outcome the wording must never produce.
        var terminated = await _revocationService.TerminateSessionsByDeviceAsync(
            request.UserId, device.DeviceHash, TerminationReason, cancellationToken);

        await _deviceRepository.DeleteAsync(request.UserId, request.DeviceId, cancellationToken);

        _logger.LogInformation(
            "User {UserId} forgot device {DeviceId}, ending {SessionCount} sessions",
            request.UserId, request.DeviceId, terminated);

        return terminated;
    }
}
