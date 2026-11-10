using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Reauthenticate;

/// <summary>
/// A signed-in person proving themself again inside the current session, with no new sign-in:
/// the password, or the emailed code of an account without one, and the second step a sign-in of
/// the same account would ask for. The passkey's own request is
/// <c>ReauthenticateWithPasskeyCommand</c>.
/// </summary>
/// <param name="UserId">The authenticated user.</param>
/// <param name="CurrentSessionId">The session the access token belongs to (its <c>sid</c>): the session stamped.</param>
/// <param name="IdpSessionToken">The caller's own SSO cookie value, if the browser presented one: raised too.</param>
/// <param name="Password">The password, when it is the first proof.</param>
/// <param name="EmailCode">The emailed code, when it is the first proof.</param>
/// <param name="SecondStepCode">
/// The second step's code: from the authenticator app, emailed for this proof, or a recovery code
/// when <paramref name="UseRecoveryCode"/> is true.
/// </param>
/// <param name="UseRecoveryCode">Whether <paramref name="SecondStepCode"/> is a recovery code.</param>
/// <param name="IpAddress">The caller's address.</param>
/// <param name="UserAgent">The caller's user agent.</param>
/// <param name="DeviceId">The browser's own identifier.</param>
public record ReauthenticateCommand(
    Guid UserId,
    Guid? CurrentSessionId,
    string? IdpSessionToken,
    string? Password,
    string? EmailCode,
    string? SecondStepCode,
    bool UseRecoveryCode,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId) : IRequest<ErrorOr<Success>>
{
    // Every proof and the SSO cookie are secrets; the synthesized ToString would print them into
    // any log line or assertion message the command reaches.
    public override string ToString() =>
        $"ReauthenticateCommand {{ UserId = {UserId}, CurrentSessionId = {CurrentSessionId}, UseRecoveryCode = {UseRecoveryCode} }}";
}
