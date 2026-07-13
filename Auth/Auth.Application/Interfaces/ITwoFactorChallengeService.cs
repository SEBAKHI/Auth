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
    /// the user are invalidated.
    /// </summary>
    /// <param name="user">The user who passed primary authentication.</param>
    /// <param name="ipAddress">The client's IP address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plain challenge token (only its hash is stored).</returns>
    Task<string> CreateChallengeAsync(User user, string? ipAddress, CancellationToken cancellationToken);
}
