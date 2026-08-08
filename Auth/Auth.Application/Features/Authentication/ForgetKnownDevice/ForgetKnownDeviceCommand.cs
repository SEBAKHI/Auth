using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ForgetKnownDevice;

/// <summary>
/// Command to forget a browser: remove its recognition record and end every
/// session it still holds.
/// </summary>
/// <param name="UserId">The owner of the device.</param>
/// <param name="DeviceId">The device row to forget.</param>
/// <param name="CurrentSessionId">
/// The caller's own session. The browser holding it cannot be forgotten — that
/// would sign the user out from a control whose label does not say so.
/// </param>
/// <returns>The number of sessions ended.</returns>
public record ForgetKnownDeviceCommand(
    Guid UserId,
    Guid DeviceId,
    Guid? CurrentSessionId = null) : IRequest<ErrorOr<int>>;
