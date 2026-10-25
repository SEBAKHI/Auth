using Auth.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Signing every other session and browser out after a second factor was given up
/// (S24): the emailed code switched off, or either factor replaced by the other.
/// </summary>
/// <remarks>
/// The change is already committed when this runs, so a client that disconnects must
/// not stop it (<see cref="CancellationToken.None"/>), and a failed revocation must
/// not cost the owner the notice or the request its success: it is logged for an
/// operator, with the reason that names the change.
/// </remarks>
public static class SecondFactorChangeRevocation
{
    /// <summary>
    /// Revokes every credential of <paramref name="userId"/> except the caller's own
    /// session and SSO cookie, recording <paramref name="reason"/>.
    /// </summary>
    public static async Task RevokeOtherSessionsAsync(
        this ICredentialRevocationService revocation,
        Guid userId,
        Guid? currentSessionId,
        string? idpSessionToken,
        string reason,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            await revocation.RevokeCredentialsAsync(
                userId,
                currentSessionId,
                idpSessionToken,
                revokedBy: userId,
                reason,
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "The second factor of user {UserId} changed ({Reason}) but the other sessions could not be signed out",
                userId, reason);
        }
    }
}
