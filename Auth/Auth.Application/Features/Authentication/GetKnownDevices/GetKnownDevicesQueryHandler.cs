using Auth.Application.DTOs;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetKnownDevices;

/// <summary>
/// Handler for the known-devices query.
/// </summary>
public class GetKnownDevicesQueryHandler
    : IRequestHandler<GetKnownDevicesQuery, ErrorOr<IReadOnlyList<KnownDeviceDto>>>
{
    private readonly IUserKnownDeviceRepository _deviceRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly ILogger<GetKnownDevicesQueryHandler> _logger;

    public GetKnownDevicesQueryHandler(
        IUserKnownDeviceRepository deviceRepository,
        IUserSessionRepository sessionRepository,
        ILogger<GetKnownDevicesQueryHandler> logger)
    {
        _deviceRepository = deviceRepository;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<KnownDeviceDto>>> Handle(
        GetKnownDevicesQuery request,
        CancellationToken cancellationToken)
    {
        var devices = await _deviceRepository.GetForUserAsync(request.UserId, cancellationToken);
        if (devices.Count == 0)
        {
            return Array.Empty<KnownDeviceDto>();
        }

        // The session rows carry the form factor and the live counts; the ledger
        // carries the identity and the dates. Joined in memory on the shared
        // signature rather than in SQL because a user has tens of sessions, not
        // thousands, and this keeps the entity free of query-only projections.
        var sessions = await _sessionRepository.GetActiveSessionsForUserAsync(
            request.UserId, sortBy: null, SortDirection.Asc, cancellationToken);

        var byHash = sessions
            .Where(s => s.DeviceHash is not null)
            .GroupBy(s => s.DeviceHash!)
            .ToDictionary(g => g.Key, g => g.ToList());

        var dtos = devices.Select(device =>
        {
            byHash.TryGetValue(device.DeviceHash, out var deviceSessions);
            deviceSessions ??= [];

            return new KnownDeviceDto
            {
                Id = device.Id,
                DeviceName = device.DeviceName,
                // Taken from the most recent live session rather than stored on
                // the ledger: the ledger predates the column, and the form factor
                // is a property of the sign-in, not of the signature.
                DeviceType = deviceSessions
                    .OrderByDescending(s => s.LastActivityAt)
                    .Select(s => s.DeviceType)
                    .FirstOrDefault(DeviceType.Unknown),
                FirstSeenAt = device.FirstSeenAt,
                LastSeenAt = device.LastSeenAt,
                ActiveSessionCount = deviceSessions.Count,
                IsCurrent = request.CurrentSessionId.HasValue
                    && deviceSessions.Any(s => s.Id == request.CurrentSessionId.Value)
            };
        }).ToList();

        _logger.LogDebug(
            "Retrieved {DeviceCount} known devices for user {UserId}",
            dtos.Count, request.UserId);

        return dtos;
    }
}
