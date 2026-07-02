using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetUserSessions;

/// <summary>
/// Query to get a user's active sessions.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="CurrentSessionId">The ID of the current session (to mark as current).</param>
/// <param name="SortBy">Optional allow-listed sort field; null keeps the default order.</param>
/// <param name="SortDirection">Direction applied when <paramref name="SortBy"/> is set.</param>
public record GetUserSessionsQuery(
    Guid UserId,
    Guid? CurrentSessionId = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<SessionDto>>>;
