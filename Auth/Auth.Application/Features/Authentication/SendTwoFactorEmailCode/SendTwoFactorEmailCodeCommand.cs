using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.SendTwoFactorEmailCode;

/// <summary>
/// Command to email the code an account enters before it switches on its FIRST
/// second factor.
/// </summary>
/// <param name="UserId">The ID of the user setting up two-factor authentication.</param>
/// <param name="CurrentSessionId">
/// The session the access token belongs to (its <c>sid</c>), measured by the
/// re-authentication check.
/// </param>
/// <param name="IpAddress">The caller's address, named in the email and kept for audit.</param>
public record SendTwoFactorEmailCodeCommand(
    Guid UserId,
    Guid? CurrentSessionId,
    string? IpAddress) : IRequest<ErrorOr<TwoFactorEmailCodeResponse>>;

/// <summary>
/// Whether switching two-factor on needs the emailed code, and where it went.
/// </summary>
/// <param name="EmailCodeRequired">
/// False when no code is needed right now (email is off, or the switch is): nothing
/// was sent, and enable proceeds without one.
/// </param>
/// <param name="SentTo">The address the code went to, masked; null when none was sent.</param>
/// <param name="ExpiresAt">When the code stops being accepted (UTC); null when none was sent.</param>
public record TwoFactorEmailCodeResponse(
    bool EmailCodeRequired,
    string? SentTo,
    DateTime? ExpiresAt);
