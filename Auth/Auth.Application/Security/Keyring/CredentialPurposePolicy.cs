using System.Collections.Frozen;
using Auth.Domain.Enums;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// What the keyring knows about one credential purpose: the label its subkey is
/// derived under, how long an old key's fingerprints stay acceptable, and how
/// the fingerprint is stored.
/// </summary>
/// <param name="DerivationLabel">The SP 800-108 label of the purpose subkey. Fixed forever: changing it changes every subkey fingerprint.</param>
/// <param name="Overlap">The overlap window after a rotation.</param>
/// <param name="Format">The stored shape.</param>
public sealed record CredentialPurposeRule(
    string DerivationLabel,
    CredentialOverlapRule Overlap,
    ICredentialHashFormat Format);

/// <summary>
/// The one table of every credential purpose (KR4, KR9). The hasher reads it and
/// nothing else, so adding a purpose is one row here and one enum member.
/// </summary>
/// <remarks>
/// Each limit is the LONGEST lifetime the code can configure for that credential
/// today, not its default: a limit shorter than a credential's possible life
/// would drop still-valid credentials at the first rotation.
/// </remarks>
public static class CredentialPurposePolicy
{
    private static readonly ICredentialHashFormat Plain = new PlainCredentialHashFormat();

    private static readonly FrozenDictionary<CredentialPurpose, CredentialPurposeRule> Rules =
        new Dictionary<CredentialPurpose, CredentialPurposeRule>
        {
            // Refresh tokens live up to 365 days and SSO sessions up to 90. Their real
            // overlap is the previous key's VerifyUntilUtc, computed from the longest
            // live row at promote and capped at 30 days (KR4); this limit is that cap.
            [CredentialPurpose.RefreshToken] = Bounded("AuthSystem/credential-hash/RefreshToken/v1", PurposeLimitedOverlap.LegacyCap),
            [CredentialPurpose.IdpSession] = Bounded("AuthSystem/credential-hash/IdpSession/v1", PurposeLimitedOverlap.LegacyCap),
            // IdentityProvider:AuthorizationCodeLifetimeSeconds, Max 300.
            [CredentialPurpose.AuthorizationCode] = Bounded("AuthSystem/credential-hash/AuthorizationCode/v1", TimeSpan.FromSeconds(300)),
            // IdentityProvider:AuthorizationRequestLifetimeMinutes, clamped to 5..1440.
            [CredentialPurpose.AuthorizationRequestHandle] = Bounded("AuthSystem/credential-hash/AuthorizationRequestHandle/v1", TimeSpan.FromMinutes(1440)),
            // Email:ResetTokenExpirationMinutes, Max 1440.
            [CredentialPurpose.PasswordReset] = Bounded("AuthSystem/credential-hash/PasswordReset/v1", TimeSpan.FromMinutes(1440)),
            // Invitations expire 7 days after they are sent or re-sent.
            [CredentialPurpose.Invitation] = Bounded("AuthSystem/credential-hash/Invitation/v1", TimeSpan.FromDays(7)),
            // TwoFactorChallenge.DefaultLifetimeMinutes, the only lifetime any caller uses.
            [CredentialPurpose.TwoFactorChallenge] = Bounded("AuthSystem/credential-hash/TwoFactorChallenge/v1", TimeSpan.FromMinutes(5)),
            // The nonce cookie's own lifetime (ExternalNonceCookie.Lifetime).
            [CredentialPurpose.ExternalNonce] = Bounded("AuthSystem/credential-hash/ExternalNonce/v1", TimeSpan.FromMinutes(10)),
            // IdentityProvider:StepUpTicketLifetimeMinutes, clamped to 1..60.
            [CredentialPurpose.StepUpTicket] = Bounded("AuthSystem/credential-hash/StepUpTicket/v1", TimeSpan.FromMinutes(60)),
            // Email:OtpExpirationMinutes and AccountDeletion:OtpExpirationMinutes, Max 60.
            [CredentialPurpose.Otp] = Bounded("AuthSystem/credential-hash/Otp/v1", TimeSpan.FromMinutes(60)),
            [CredentialPurpose.TwoFactorBind] = Bounded("AuthSystem/credential-hash/TwoFactorBind/v1", TimeSpan.FromMinutes(60)),
            // Re-derived with every code, which lives at most Email:OtpExpirationMinutes.
            [CredentialPurpose.PendingRegistrationHandle] = Bounded("AuthSystem/credential-hash/PendingRegistrationHandle/v1", TimeSpan.FromMinutes(60)),
            [CredentialPurpose.WebhookKey] = new CredentialPurposeRule(
                "AuthSystem/credential-hash/WebhookKey/v1", new WebhookKeyGraceOverlap(), new WebhookKeyHashFormat()),
        }.ToFrozenDictionary();

    /// <summary>Every purpose the table covers.</summary>
    public static IReadOnlyCollection<CredentialPurpose> Purposes => Rules.Keys;

    /// <summary>The rule of <paramref name="purpose"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A purpose without a row: a programming error.</exception>
    public static CredentialPurposeRule For(CredentialPurpose purpose) =>
        Rules.TryGetValue(purpose, out var rule)
            ? rule
            : throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "No keyring rule for this credential purpose.");

    // The label is written out per row, never derived from the member name, so renaming a
    // member changes no subkey; PurposeSubkeyDerivationTests pins every label.
    private static CredentialPurposeRule Bounded(string derivationLabel, TimeSpan limit) =>
        new(derivationLabel, new PurposeLimitedOverlap(limit), Plain);
}
