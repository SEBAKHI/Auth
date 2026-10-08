using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.StepUpTwoFactor;

/// <summary>
/// Command to prove the second factor inside the current session — the step-up a
/// platform administrator who signed in with the password alone completes before
/// the next refresh returns the platform authority.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Code">
/// The code that proves the factor: from the authenticator app, or one of the
/// user's recovery codes when <paramref name="UseRecoveryCode"/> is true.
/// </param>
/// <param name="UseRecoveryCode">Whether <paramref name="Code"/> is a recovery code.</param>
/// <param name="CurrentSessionId">
/// The session the access token belongs to (its <c>sid</c>): the session upgraded.
/// </param>
/// <param name="IdpSessionToken">
/// The caller's own SSO cookie value, if the browser presented one: its SSO
/// session is upgraded too.
/// </param>
/// <param name="IpAddress">The caller's address, for the reused-code log lines only.</param>
public record StepUpTwoFactorCommand(
    Guid UserId,
    string Code,
    bool UseRecoveryCode,
    Guid? CurrentSessionId,
    string? IdpSessionToken,
    string? IpAddress) : IRequest<ErrorOr<Success>>
{
    // The code and the SSO cookie are secrets; the synthesized ToString would
    // print them into any log line or assertion message the command reaches.
    public override string ToString() =>
        $"StepUpTwoFactorCommand {{ UserId = {UserId}, UseRecoveryCode = {UseRecoveryCode}, CurrentSessionId = {CurrentSessionId} }}";
}
