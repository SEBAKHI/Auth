using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// The WebAuthn ceremonies of a passkey (S20, PK1): the options a browser passes to
/// <c>navigator.credentials.create()</c> and <c>navigator.credentials.get()</c>, and the check
/// of what the authenticator answered. The one port over the WebAuthn library, which lives in
/// Infrastructure alone.
/// </summary>
/// <remarks>
/// <para>
/// Options and responses cross this port as the JSON the browser exchanges, so nothing in
/// Application knows a type of the library. The options are stored with the challenge they
/// carry (<c>WebAuthnChallenges.OptionsJson</c>) and handed back to the check unchanged:
/// the check compares the response with exactly what was offered.
/// </para>
/// <para>
/// Every rejection of what the client sent — a malformed or mismatched field, a wrong
/// origin, a missing user verification, an algorithm other than ES256 or RS256 — is one
/// opaque error, <c>Passkey.VerificationFailed</c> (PK10). Anything else (the library
/// failing on its own) propagates, and the request answers 500.
/// </para>
/// </remarks>
public interface IWebAuthnVerifier
{
    /// <summary>
    /// The options of a registration (PK5): a discoverable credential, user verification
    /// required, no attestation, the account's existing credentials excluded, ES256 and
    /// RS256 only, five minutes to answer.
    /// </summary>
    /// <param name="relyingParty">The relying party the credential is bound to.</param>
    /// <param name="subject">The account the credential is made for.</param>
    /// <param name="excludeCredentialIds">The account's live credentials, which the authenticator must not duplicate.</param>
    /// <returns>The options, as the JSON the browser receives and the check reads back.</returns>
    string CreateRegistrationOptions(
        PasskeyRelyingParty relyingParty,
        PasskeyRegistrationSubject subject,
        IReadOnlyList<byte[]> excludeCredentialIds);

    /// <summary>
    /// Checks a registration response against the options it answers.
    /// </summary>
    /// <param name="relyingParty">The relying party now: its RP ID and its exact origins.</param>
    /// <param name="optionsJson">The options as they were offered.</param>
    /// <param name="responseJson">The browser's <c>PublicKeyCredential</c>, as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verified credential, or <c>Passkey.VerificationFailed</c>.</returns>
    Task<ErrorOr<VerifiedPasskeyCredential>> VerifyRegistrationAsync(
        PasskeyRelyingParty relyingParty,
        string optionsJson,
        string responseJson,
        CancellationToken cancellationToken);

    /// <summary>
    /// The options of a sign-in (S20 PR B, PK5): no credential named (<c>allowCredentials</c>
    /// empty, so the device offers the discoverable passkeys it holds for the RP ID), user
    /// verification required, a 32-byte challenge, five minutes to answer.
    /// </summary>
    /// <param name="relyingParty">The relying party the sign-in is for.</param>
    /// <returns>The options, as the JSON the browser receives and the check reads back.</returns>
    string CreateAssertionOptions(PasskeyRelyingParty relyingParty);

    /// <summary>
    /// Checks a sign-in response against the options it answers and the passkey it names: the
    /// exact origin, the stored options' challenge and RP ID, user verification, the signature
    /// over the stored public key, and the stored counter.
    /// </summary>
    /// <remarks>
    /// The stored key is the server's own: one that cannot be read, or that is not an EC2 or
    /// RSA key (registration stores no other), is a fault of the server and throws — it is
    /// never answered as the client's <c>Passkey.VerificationFailed</c> (N4).
    /// </remarks>
    /// <param name="relyingParty">The relying party now: its RP ID and its exact origins.</param>
    /// <param name="optionsJson">The options as they were offered.</param>
    /// <param name="responseJson">The browser's <c>PublicKeyCredential</c>, as JSON.</param>
    /// <param name="storedCredential">The passkey the response names, as stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// What the verified assertion carries — <see cref="VerifiedPasskeyAssertion.CounterRegressed"/>
    /// when the signature verified but the library refused the counter — or
    /// <c>Passkey.VerificationFailed</c>.
    /// </returns>
    Task<ErrorOr<VerifiedPasskeyAssertion>> VerifyAssertionAsync(
        PasskeyRelyingParty relyingParty,
        string optionsJson,
        string responseJson,
        StoredPasskeyCredential storedCredential,
        CancellationToken cancellationToken);
}

/// <summary>
/// A passkey as the sign-in check needs it: never the credential id, which the response
/// already names.
/// </summary>
/// <param name="PasskeyId">The row's id: the only thing about it a log line names.</param>
/// <param name="PublicKey">The COSE public key stored at registration.</param>
/// <param name="SignCount">The counter last accepted.</param>
public sealed record StoredPasskeyCredential(Guid PasskeyId, byte[] PublicKey, long SignCount);

/// <summary>
/// A sign-in the library verified: the signature over the stored key is valid.
/// </summary>
/// <param name="SignCount">The authenticator's signature counter in the assertion.</param>
/// <param name="IsBackupEligible">The assertion's BE flag.</param>
/// <param name="IsBackedUp">The assertion's BS flag.</param>
/// <param name="CounterRegressed">
/// The library refused the counter (it did not grow) after the signature verified — a copied
/// or broken authenticator, never a forged response (PK9). The sign-in is refused and the
/// passkey sealed.
/// </param>
public sealed record VerifiedPasskeyAssertion(
    uint SignCount,
    bool IsBackupEligible,
    bool IsBackedUp,
    bool CounterRegressed);

/// <summary>
/// The relying party a passkey is bound to (PK2, PK4).
/// </summary>
/// <param name="RpId">The registrable domain the credential is scoped to (<c>Passkeys:RpId</c>).</param>
/// <param name="RpName">The name an authenticator may show the person.</param>
/// <param name="Origins">
/// The exact origins a ceremony may come from: the platform's own browser apps
/// (<c>IdentityProvider:FirstPartySpaOrigins</c>), never the CORS list.
/// </param>
public sealed record PasskeyRelyingParty(string RpId, string RpName, IReadOnlySet<string> Origins);

/// <summary>
/// The account a passkey is made for.
/// </summary>
/// <param name="UserHandle">The account's WebAuthn user handle: 64 random bytes, never its id (PK17).</param>
/// <param name="Name">The name an authenticator shows to pick the account (its address).</param>
/// <param name="DisplayName">The account's display name.</param>
public sealed record PasskeyRegistrationSubject(byte[] UserHandle, string Name, string DisplayName);

/// <summary>
/// A credential the library verified at registration.
/// </summary>
/// <param name="CredentialId">The credential id, compared as bytes.</param>
/// <param name="PublicKey">The COSE public key.</param>
/// <param name="SignCount">The authenticator's signature counter at registration.</param>
/// <param name="IsBackupEligible">The BE flag: the credential may be synced to other devices.</param>
/// <param name="IsBackedUp">The BS flag: the credential is synced now.</param>
public sealed record VerifiedPasskeyCredential(
    byte[] CredentialId,
    byte[] PublicKey,
    uint SignCount,
    bool IsBackupEligible,
    bool IsBackedUp);
