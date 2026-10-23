using Auth.Domain.Entities;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// The one source of codes emailed as a second factor (S24): every sign-in and every
/// recovery that asks for one goes through here, so the limits on sending are
/// enforced in one place.
/// </summary>
public interface IEmailCodeSender
{
    /// <summary>
    /// Emails a fresh six-digit code for <paramref name="challenge"/> to the account's
    /// address. The new code replaces the live one, and the challenge then expires
    /// <c>TwoFactor:EmailCodeLifetimeMinutes</c> after this send.
    /// </summary>
    /// <remarks>
    /// Nothing is sent while <c>Email:Enabled</c> is false, read for this request: a
    /// code that cannot arrive would only spend the person's sends. The reservation is
    /// one statement whose conditions are every limit (the challenge's three sends, the
    /// account's per-window and daily caps, a locked emailed code), so a burst of
    /// requests never gets past them. The code is never returned or logged.
    /// </remarks>
    /// <param name="user">The account the challenge belongs to.</param>
    /// <param name="challenge">The live challenge the code completes.</param>
    /// <param name="ipAddress">The requesting client address, for the email.</param>
    /// <param name="hardLimit">
    /// The consecutive-failure limit the emailed code must be under for a code to be
    /// sent at all (Hard′), or null where none applies.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// When the code expires, the masked address it went to and how many more sends
    /// the challenge allows; or <c>TwoFactor.EmailCodeUnavailable</c> (email off, or no
    /// emailed code on the account), <c>TwoFactor.EmailCodeTooManyRequests</c> (a send
    /// limit), <c>TwoFactor.LockedOut</c> or <c>TwoFactor.EmailCodeLocked</c> (the
    /// emailed code is locked), <c>TwoFactor.ChallengeInvalid</c> (the challenge ended),
    /// or <c>TwoFactor.EmailCodeSendFailed</c>.
    /// </returns>
    Task<ErrorOr<EmailCodeSent>> SendAsync(
        User user,
        TwoFactorChallenge challenge,
        string? ipAddress,
        int? hardLimit,
        CancellationToken cancellationToken);
}

/// <summary>
/// An emailed second-factor code that was reserved and handed to the notification
/// service.
/// </summary>
/// <param name="ExpiresAt">When the code stops being accepted (UTC).</param>
/// <param name="MaskedEmail">The address it went to, masked.</param>
/// <param name="SendsRemaining">How many more codes this challenge may email.</param>
public sealed record EmailCodeSent(DateTime ExpiresAt, string MaskedEmail, int SendsRemaining);
