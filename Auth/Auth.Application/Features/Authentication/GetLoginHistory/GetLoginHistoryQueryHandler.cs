using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetLoginHistory;

/// <summary>
/// Handler for the login-history query.
/// </summary>
public class GetLoginHistoryQueryHandler
    : IRequestHandler<GetLoginHistoryQuery, ErrorOr<IReadOnlyList<LoginAttemptDto>>>
{
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IGeoIpLookup _geoIpLookup;
    private readonly ILogger<GetLoginHistoryQueryHandler> _logger;

    public GetLoginHistoryQueryHandler(
        ILoginAttemptRepository loginAttemptRepository,
        IGeoIpLookup geoIpLookup,
        ILogger<GetLoginHistoryQueryHandler> logger)
    {
        _loginAttemptRepository = loginAttemptRepository;
        _geoIpLookup = geoIpLookup;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<LoginAttemptDto>>> Handle(
        GetLoginHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var attempts = await _loginAttemptRepository.GetRecentByUserAsync(
            request.UserId, request.Take, cancellationToken);

        var dtos = attempts.Select(attempt =>
        {
            // Parsed here rather than stored: these rows long predate the device
            // columns, and the raw agent is not something to put in front of a
            // user anyway.
            var parsed = UserAgentParser.Parse(attempt.UserAgent);

            return new LoginAttemptDto
            {
                Id = attempt.Id,
                AttemptedAt = attempt.AttemptedAt,
                IsSuccess = attempt.IsSuccess,
                FailureReason = attempt.FailureReason,
                IpAddress = attempt.IpAddress,
                // Resolved on read, unlike the session row which is stamped on
                // write. Login attempts are recorded from three separate handlers
                // and none of them has ever set this column, so reading through
                // to the lookup is what gives the existing history any locations
                // at all. The stored value still wins where one exists.
                Location = attempt.Location ?? _geoIpLookup.Resolve(attempt.IpAddress),
                DeviceName = parsed.Describe(),
                DeviceType = parsed.DeviceType
            };
        }).ToList();

        _logger.LogDebug(
            "Retrieved {AttemptCount} login attempts for user {UserId}",
            dtos.Count, request.UserId);

        return dtos;
    }
}
