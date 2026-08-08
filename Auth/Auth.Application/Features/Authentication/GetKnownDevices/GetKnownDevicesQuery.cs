using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetKnownDevices;

/// <summary>
/// Query for the browsers a user has signed in from.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="CurrentSessionId">
/// The caller's own session, used to mark the browser they are reading this on.
/// That browser is the one they must not be allowed to forget.
/// </param>
public record GetKnownDevicesQuery(
    Guid UserId,
    Guid? CurrentSessionId = null) : IRequest<ErrorOr<IReadOnlyList<KnownDeviceDto>>>;
