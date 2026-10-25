namespace Auth.Domain.Enums;

/// <summary>
/// The kind of opaque credential a keyed fingerprint is computed for. Each kind
/// is fingerprinted under its own key, so a fingerprint of one kind can never be
/// presented as another, and a leak or a rotation of one kind's window does not
/// reach the others.
/// </summary>
/// <remarks>
/// A new opaque credential takes a NEW member; it never reuses an existing one.
/// The values are explicit and never renumbered. None is persisted today, but the
/// derivation label of each member is fixed in <c>CredentialPurposePolicy</c>, so
/// renaming a member changes nothing that is already stored.
/// </remarks>
public enum CredentialPurpose
{
    /// <summary>Refresh tokens (RefreshTokens.TokenHash).</summary>
    RefreshToken = 1,

    /// <summary>The SSO cookie's session token (IdpSessions.TokenHash).</summary>
    IdpSession = 2,

    /// <summary>OAuth authorization codes (AuthorizationCodes.CodeHash).</summary>
    AuthorizationCode = 3,

    /// <summary>The handle of a parked authorize request (AuthorizationRequests.HandleHash).</summary>
    AuthorizationRequestHandle = 4,

    /// <summary>Password-reset links (PasswordResetTokens.TokenHash).</summary>
    PasswordReset = 5,

    /// <summary>Organization invitation links (OrganizationInvitations.Token).</summary>
    Invitation = 6,

    /// <summary>The token of a pending two-factor sign-in (TwoFactorChallenges.TokenHash).</summary>
    TwoFactorChallenge = 7,

    /// <summary>The cookie half of a provider sign-in nonce.</summary>
    ExternalNonce = 8,

    /// <summary>The signature of a step-up ticket.</summary>
    StepUpTicket = 9,

    /// <summary>Emailed confirmation codes.</summary>
    Otp = 10,

    /// <summary>The emailed code that proves the first factor while binding a second one.</summary>
    TwoFactorBind = 11,

    /// <summary>The handle a pending registration is answered with.</summary>
    PendingRegistrationHandle = 12,

    /// <summary>Webhook keys (WebhookKeys.KeyHash).</summary>
    WebhookKey = 13,

    /// <summary>The code emailed as a second factor (TwoFactorChallenges.EmailCodeHash).</summary>
    TwoFactorEmailCode = 14
}
