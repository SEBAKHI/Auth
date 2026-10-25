using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.SendSignInEmailCode;
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
    /// The plain challenge token (only its hash is stored), the methods the sign-in
    /// may be completed with (S24), and the emailed code when one was sent: a
    /// challenge that offers the emailed code emails it here, once (D-243). A refused
    /// or failed send never fails the sign-in; the challenge then carries no code,
    /// and the client asks for one.
    /// </returns>
    Task<IssuedTwoFactorChallenge> CreateChallengeAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        AuthenticationMethods primaryMethod,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether a provider sign-in already proved what the account's second factor
    /// would: the factor is the emailed code (and not the authenticator app), the
    /// provider controls the mailbox (<see cref="ExternalUserInfo.ControlsMailbox"/>),
    /// and its address is the account's own. Such a sign-in asks for no second step
    /// (D-243); any other two-factor account is challenged as always.
    /// </summary>
    /// <param name="user">The account the provider sign-in resolved to.</param>
    /// <param name="provider">What the provider's token said.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ProviderProvesMailboxAsync(
        User user,
        ExternalUserInfo provider,
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
/// <param name="EmailCode">
/// The emailed code sent when the challenge was opened (D-243); null when the methods
/// do not offer it, or the send was refused or failed.
/// </param>
public sealed record IssuedTwoFactorChallenge(
    string Token,
    IReadOnlyList<string>? Methods,
    EmailCodeSent? EmailCode = null)
{
    /// <summary>
    /// The answer every gate gives a sign-in that must complete the second step: the
    /// challenge, its methods, and where the emailed code went when one was sent.
    /// </summary>
    public LoginResponse ToPendingLogin() => new()
    {
        RequiresTwoFactor = true,
        TwoFactorChallengeToken = Token,
        TwoFactorMethods = Methods,
        TwoFactorEmailCode = EmailCode is { } sent ? SignInEmailCodeResponse.From(sent) : null
    };

    // The token is a bearer secret until the sign-in completes.
    public override string ToString() =>
        $"IssuedTwoFactorChallenge {{ Methods = {string.Join(",", Methods ?? [])}, EmailCodeSent = {EmailCode is not null} }}";
}
