using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.ReauthenticateWithPasskey;

/// <summary>
/// A signed-in person proving themself again with a passkey, with no new sign-in: the browser's
/// answer to the options of <c>BeginPasskeyAssertionCommand</c>, checked against the challenge they
/// carried. A passkey the device verified the person for proves two factors on its own (M3).
/// </summary>
/// <param name="UserId">The authenticated user.</param>
/// <param name="CurrentSessionId">The session the access token belongs to (its <c>sid</c>): the session stamped.</param>
/// <param name="IdpSessionToken">The caller's own SSO cookie value, if the browser presented one: raised too.</param>
/// <param name="ChallengeHandle">The handle the challenge cookie carried, or null when there was none.</param>
/// <param name="ChallengeHandleRepeated">The request carried the challenge cookie more than once: refused, and nothing is spent.</param>
/// <param name="ResponseJson">The browser's <c>PublicKeyCredential</c>, as JSON.</param>
/// <param name="IpAddress">The caller's address, for the audit row.</param>
/// <param name="UserAgent">The caller's user agent, for the audit row.</param>
public record ReauthenticateWithPasskeyCommand(
    Guid UserId,
    Guid? CurrentSessionId,
    string? IdpSessionToken,
    string? ChallengeHandle,
    bool ChallengeHandleRepeated,
    string ResponseJson,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<Success>>
{
    // The handle, the SSO cookie and the signed answer stay out of every log line (PK16).
    public override string ToString() =>
        $"ReauthenticateWithPasskeyCommand {{ UserId = {UserId}, CurrentSessionId = {CurrentSessionId} }}";
}
