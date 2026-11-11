using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// The session a passkey may be added from (PK7, T-A4), checked in this order, before
/// anything is minted, counted or written:
/// <list type="number">
/// <item>a recent sign-in (<c>TwoFactor:ReauthenticationMaxAgeMinutes</c>, X02);</item>
/// <item>that proved a first factor (D-215-1, REVIEW 110 F5): a password account signed in
/// by emailed code proved its mailbox only, and a passkey added from there would be a door
/// of the mailbox holder's own;</item>
/// <item>when the account already holds a qualifying second factor (FA2), a session that
/// proved two (FA11 (11-2)): a further factor needs the proof the account already has;</item>
/// <item>when it holds none, the fresh code emailed to its confirmed address, checked before
/// the bind (ARR-1049, X02's code) — so a first passkey needs email sending on: with it off
/// the gate refuses (<c>Passkey.EmailRequired</c>, REVIEW 150 F13, D-150-3 (c)). Unless the
/// session proved the mailbox with an emailed code within
/// <c>Passkeys:CodeSignInProofMinutes</c> (OI-280 PR B), by its sign-in or in the window: that
/// code already proved the mailbox, so a second one is not asked for (D-S20-14, D-S20-21).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Why email off refuses rather than binds with the notice alone, as the authenticator app
/// does (RV-P8-4): a thief's app locks the owner out at the next sign-in, so it is found, and
/// an administrator's reset then removes everything. A thief's passkey is a separate door:
/// the owner keeps signing in with the password and sees nothing, no notice can reach them,
/// and once it is 72 hours old a password change no longer revokes it. So with email off the
/// way in is the app first, then a passkey from a session that proved both. Removing a
/// passkey is unchanged, so a lost passkey traps nobody.
/// </para>
/// <para>
/// <c>Email:Enabled</c> is read once per request and decides both the refusal and whether
/// the code is asked for. The gate runs at options, at the send and again at verify, so a
/// switch flipped between options and verify lets nothing through.
/// </para>
/// <para>
/// An account without a password may add a passkey (D-S20-2 (a)): its emailed code is its
/// first factor (OI-215), and nothing removes its address, so a passkey is never its only
/// way in. NIST SP 800-63B-4 §4.1.2.1: binding a new authenticator needs the account's
/// highest proof, and the owner is notified independently.
/// </para>
/// <para>
/// The mailbox proof of a sign-in by code (OI-280 PR B). The code a session signed in with, or
/// gave in place in the window, proved the mailbox when it was accepted; within
/// <c>Passkeys:CodeSignInProofMinutes</c> of that (5 by default, 1–15) the bind asks for no second
/// one. The time is the one <see cref="RecentSession"/> carries: the sign-in while that is
/// recent, the in-place proof past it (D-B2). A session that also carries an authenticator-app
/// code, a recovery code or a passkey never counts its code (D-B3): a code-first two-step sign-in
/// proved its code when the challenge opened, minutes before the session began, and such an
/// account needs no code while its factor stays. The re-authentication window still comes first,
/// so it stays the outer limit, and the first-factor step above still asks a password account
/// for its password. The price, accepted by the owner (D-S20-21): a session stolen within that
/// time adds a passkey without the second code. One that has not yet proved the account's
/// password must give it first; one that has (a password sign-in whose second step was the
/// emailed code, a registration that set a password, the password given in the window) needs
/// nothing more, as for an account without a password. Verify runs this again, so a time that runs out
/// between the options and the bind asks for the code there, before anything is spent.
/// </para>
/// </remarks>
public sealed class PasskeyRegistrationGate
{
    /// <summary>
    /// The bits that keep a session's emailed code from counting as the mailbox proof (D-B3): a
    /// second factor of the account's beside it, which a first passkey does not ask for anyway.
    /// </summary>
    private static readonly AuthenticationMethods SecondFactorBits =
        AuthenticationMethods.Totp.With(AuthenticationMethods.RecoveryCode).With(AuthenticationMethods.Passkey);

    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly FirstFactorEmailProofPolicy _emailProofPolicy;
    private readonly IOptionsMonitor<PasskeySettings> _passkeySettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PasskeyRegistrationGate> _logger;

    public PasskeyRegistrationGate(
        IReauthenticationGuard reauthenticationGuard,
        ITwoFactorStateStore twoFactorStateStore,
        FirstFactorEmailProofPolicy emailProofPolicy,
        IOptionsMonitor<PasskeySettings> passkeySettings,
        TimeProvider timeProvider,
        ILogger<PasskeyRegistrationGate> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _twoFactorStateStore = twoFactorStateStore;
        _emailProofPolicy = emailProofPolicy;
        _passkeySettings = passkeySettings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Checks the session, and decides whether the bind needs the emailed code.</summary>
    public async Task<ErrorOr<PasskeyRegistrationSession>> EnsureAsync(
        Guid userId,
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(userId, sessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        var methods = session.Value.Methods;
        if (!methods.HasPrimary)
        {
            _logger.LogInformation(
                "Adding a passkey for user {UserId} asked for a sign-in that proves a first factor (methods {AuthMethods})",
                userId, methods.Value);
            return AuthErrors.ReauthenticationRequired;
        }

        var factors = await _twoFactorStateStore.GetQualifyingFactorsAsync(userId, cancellationToken);
        if (factors.HasAny && !methods.IsMfaSatisfied)
        {
            _logger.LogInformation(
                "Adding a passkey for user {UserId} beside a qualifying second factor asked for a two-factor sign-in first",
                userId);
            return AuthErrors.ReauthenticationRequired;
        }

        // Read once: Email:Enabled is hot, and one request decides once.
        var emailOn = _emailProofPolicy.IsRequired;
        if (!factors.HasAny && !emailOn)
        {
            _logger.LogInformation(
                "Adding a first passkey for user {UserId} was refused: email sending is off, so the mailbox cannot be proved",
                userId);
            return PasskeyErrors.EmailRequired;
        }

        var emailCodeRequired = !factors.HasAny && emailOn;
        if (emailCodeRequired && MailboxJustProved(session.Value))
        {
            _logger.LogInformation(
                "Adding a first passkey for user {UserId} asks for no emailed code: the session proved the mailbox with an emailed code, within the time it counts",
                userId);
            emailCodeRequired = false;
        }

        return new PasskeyRegistrationSession(session.Value, factors, EmailCodeRequired: emailCodeRequired)
        {
            AnotherFactorRequired = !emailOn,
        };
    }

    /// <summary>
    /// Whether the session proved the mailbox with an emailed code recently enough to stand for the
    /// code a first passkey asks for: its methods carry the emailed code and no second-factor bit
    /// (D-B3), and the time it carries (the sign-in while that is recent, the last in-place proof
    /// past it) is no older than <c>Passkeys:CodeSignInProofMinutes</c>, read now and brought
    /// inside its range. Both times are the API's clock, as the window's are.
    /// </summary>
    private bool MailboxJustProved(RecentSession session)
    {
        var methods = session.Methods;
        if (!Carries(methods, AuthenticationMethods.EmailCode) || Carries(methods, SecondFactorBits))
        {
            return false;
        }

        var oldestCounted = _timeProvider.GetUtcNow().UtcDateTime - _passkeySettings.CurrentValue.CodeSignInProof;
        return session.ProvedAtUtc >= oldestCounted;
    }

    private static bool Carries(AuthenticationMethods methods, AuthenticationMethods any) =>
        (methods.Value & any.Value) != 0;
}

/// <summary>A session a passkey may be added from.</summary>
/// <param name="Session">The recent session.</param>
/// <param name="FactorsBefore">The account's qualifying factors before this passkey.</param>
/// <param name="EmailCodeRequired">Whether the bind needs the code emailed to the account's address.</param>
public sealed record PasskeyRegistrationSession(
    RecentSession Session,
    QualifyingSecondFactors FactorsBefore,
    bool EmailCodeRequired)
{
    /// <summary>
    /// Whether email is off, so the row is written only while the account still holds another
    /// factor, read again under a lock at the write (REVIEW-FIXES 150 F19, D-150-5 (a)).
    /// </summary>
    public bool AnotherFactorRequired { get; init; }
}
