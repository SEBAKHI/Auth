using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;

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
/// <item>when it holds none and email sending is on, the fresh code emailed to its confirmed
/// address, checked before the bind (ARR-1049, X02's code). Email off: the independent
/// notice alone (FA11), as for the authenticator app.</item>
/// </list>
/// </summary>
/// <remarks>
/// An account without a password may add a passkey (D-S20-2 (a)): its emailed code is its
/// first factor (OI-215), and nothing removes its address, so a passkey is never its only
/// way in. NIST SP 800-63B-4 §4.1.2.1: binding a new authenticator needs the account's
/// highest proof, and the owner is notified independently.
/// </remarks>
public sealed class PasskeyRegistrationGate
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly FirstFactorEmailProofPolicy _emailProofPolicy;
    private readonly ILogger<PasskeyRegistrationGate> _logger;

    public PasskeyRegistrationGate(
        IReauthenticationGuard reauthenticationGuard,
        ITwoFactorStateStore twoFactorStateStore,
        FirstFactorEmailProofPolicy emailProofPolicy,
        ILogger<PasskeyRegistrationGate> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _twoFactorStateStore = twoFactorStateStore;
        _emailProofPolicy = emailProofPolicy;
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
        var emailCodeRequired = !factors.HasAny && _emailProofPolicy.IsRequired;
        return new PasskeyRegistrationSession(session.Value, factors, emailCodeRequired);
    }
}

/// <summary>A session a passkey may be added from.</summary>
/// <param name="Session">The recent session.</param>
/// <param name="FactorsBefore">The account's qualifying factors before this passkey.</param>
/// <param name="EmailCodeRequired">Whether the bind needs the code emailed to the account's address.</param>
public sealed record PasskeyRegistrationSession(
    RecentSession Session,
    QualifyingSecondFactors FactorsBefore,
    bool EmailCodeRequired);
