using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetUserSessions;

/// <summary>
/// Handler for the get user sessions query.
/// </summary>
public class GetUserSessionsQueryHandler : IRequestHandler<GetUserSessionsQuery, ErrorOr<IReadOnlyList<SessionDto>>>
{
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IUserKnownDeviceRepository _deviceRepository;
    private readonly ILogger<GetUserSessionsQueryHandler> _logger;

    public GetUserSessionsQueryHandler(
        IUserSessionRepository sessionRepository,
        IUserKnownDeviceRepository deviceRepository,
        ILogger<GetUserSessionsQueryHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _deviceRepository = deviceRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<SessionDto>>> Handle(
        GetUserSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var sessions = await _sessionRepository.GetActiveSessionsForUserAsync(
            request.UserId,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        // Resolve each session's browser to the ledger row id the client can act
        // on. The signature is the join key but never leaves the server: it is
        // derived from a value the client holds, and echoing it back would let
        // anything that reads one response recognise the same browser elsewhere.
        var devices = await _deviceRepository.GetForUserAsync(request.UserId, cancellationToken);
        var deviceIdByHash = devices.ToDictionary(d => d.DeviceHash, d => d.Id);

        var sessionDtos = sessions.Select(s => new SessionDto
        {
            Id = s.Id,
            UserId = s.UserId,
            ApplicationId = s.ApplicationId,
            IpAddress = s.IpAddress,
            UserAgent = s.UserAgent,
            DeviceName = s.DeviceName,
            DeviceType = s.DeviceType,
            KnownDeviceId = s.DeviceHash is not null
                && deviceIdByHash.TryGetValue(s.DeviceHash, out var knownDeviceId)
                    ? knownDeviceId
                    : null,
            Location = s.Location,
            CreatedAt = s.CreatedAt,
            ExpiresAt = s.ExpiresAt,
            LastActivityAt = s.LastActivityAt,
            IsActive = s.IsActive,
            IsCurrent = request.CurrentSessionId.HasValue && s.Id == request.CurrentSessionId.Value
        }).ToList();

        _logger.LogDebug(
            "Retrieved {SessionCount} active sessions for user {UserId}",
            sessionDtos.Count, request.UserId);

        return sessionDtos;
    }
}
