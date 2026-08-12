using Auth.Domain.Entities;

namespace Auth.Application.Interfaces;

/// <summary>
/// Creates login-time two-factor challenges after primary authentication succeeds.
/// </summary>
public interface ITwoFactorChallengeService
{
    /// <summary>
    /// Creates a short-lived single-use challenge for the user and returns the
    /// opaque token to hand to the client. Any previous unused challenges for
    /// the user are invalidated. Also opens the sign-in ceremony's login-attempt
    /// row, so every gate that demands a second factor leaves the same trace.
    /// </summary>
    /// <param name="user">The user who passed primary authentication.</param>
    /// <param name="ipAddress">The client's IP address.</param>
    /// <param name="userAgent">
    /// The client's user agent, recorded on the ceremony row so the user's own
    /// sign-in history can name the device that produced the correct password.
    /// The challenge table has nowhere to keep it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plain challenge token (only its hash is stored).</returns>
    Task<string> CreateChallengeAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);
}
