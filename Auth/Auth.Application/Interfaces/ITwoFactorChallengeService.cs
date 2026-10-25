using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;

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
    /// <param name="primaryMethod">
    /// The first factor this request proved — the password, an external identity,
    /// or an emailed code — stored on the challenge so the verify step records the
    /// whole sign-in: this method together with the second factor it proves.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The plain challenge token (only its hash is stored), and the methods the
    /// sign-in may be completed with (S24).
    /// </returns>
    Task<IssuedTwoFactorChallenge> CreateChallengeAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        AuthenticationMethods primaryMethod,
        CancellationToken cancellationToken);
}

/// <summary>
/// A challenge just issued: what the client needs to complete the sign-in.
/// </summary>
/// <param name="Token">The plain challenge token; only its hash is stored.</param>
/// <param name="Methods">
/// The names (<c>TwoFactorMethodNames</c>) of the factors the sign-in may be completed
/// with, in the order a client offers them; null when the account's factor could not
/// be read, which a client treats as it treated every challenge before the emailed
/// code (the authenticator app, or a recovery code).
/// </param>
public sealed record IssuedTwoFactorChallenge(string Token, IReadOnlyList<string>? Methods)
{
    // The token is a bearer secret until the sign-in completes.
    public override string ToString() => $"IssuedTwoFactorChallenge {{ Methods = {string.Join(",", Methods ?? [])} }}";
}
