using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The rule for the emailed code taking the place of a bound authenticator app (S24,
/// D-T31 B, D-T34 B): the two are never bound together, so binding the emailed code
/// over the app removes the app.
/// </summary>
/// <remarks>
/// Applied when the code is asked for and again when it is confirmed: the app may be
/// bound between the two requests.
/// </remarks>
public static class AuthenticatorReplacementRule
{
    /// <summary>
    /// A session that proved two factors — otherwise whoever holds the password could
    /// swap the owner's app for a mailbox they also hold — and no platform
    /// administrator under enforcement giving up the one factor the policy counts.
    /// </summary>
    /// <returns>
    /// Success, <c>Auth.ReauthenticationRequired</c>, or <c>TwoFactor.RequiredByPolicy</c>.
    /// </returns>
    public static async Task<ErrorOr<Success>> EnsureAllowedAsync(
        IPlatformMfaPolicy platformMfaPolicy,
        Guid userId,
        RecentSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platformMfaPolicy);
        ArgumentNullException.ThrowIfNull(session);

        if (!session.Methods.IsMfaSatisfied)
        {
            return AuthErrors.ReauthenticationRequired;
        }

        return await platformMfaPolicy.IsEnforcedForUserAsync(userId, cancellationToken)
            ? TwoFactorErrors.RequiredByPolicy
            : Result.Success;
    }
}
