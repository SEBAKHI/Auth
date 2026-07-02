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
    private readonly ILogger<GetUserSessionsQueryHandler> _logger;

    public GetUserSessionsQueryHandler(
        IUserSessionRepository sessionRepository,
        ILogger<GetUserSessionsQueryHandler> logger)
    {
        _sessionRepository = sessionRepository;
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

        var sessionDtos = sessions.Select(s => new SessionDto
        {
            Id = s.Id,
            UserId = s.UserId,
            ApplicationId = s.ApplicationId,
            IpAddress = s.IpAddress,
            UserAgent = s.UserAgent,
            DeviceName = s.DeviceName,
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
