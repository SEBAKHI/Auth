namespace Auth.Application.Interfaces;

/// <summary>
/// Checks whether a candidate password appears in a known-breach corpus.
/// Implementations may call an external service (HIBP Pwned Passwords) or a local dataset.
/// </summary>
public interface IBreachedPasswordChecker
{
    /// <summary>
    /// Returns the number of times the password appears in the breach corpus (0 = not found).
    /// </summary>
    /// <remarks>
    /// Implementations should never receive or transmit the plaintext password to a third party;
    /// the HIBP implementation uses k-anonymity (only the first 5 chars of the SHA-1 hash are sent).
    /// Transport/availability failures are surfaced as exceptions so the caller can decide whether to
    /// fail open or closed.
    /// </remarks>
    Task<int> GetBreachCountAsync(string password, CancellationToken cancellationToken);
}
