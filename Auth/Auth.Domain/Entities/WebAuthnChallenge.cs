using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// One WebAuthn ceremony's challenge (S20, PK6, D-T17 A): the options offered to the
/// browser, held server-side for five minutes and spent exactly once.
/// </summary>
/// <remarks>
/// <para>
/// The browser holds only a random handle, in the <c>__Host-auth_webauthn</c> cookie; the
/// table holds the handle's keyed fingerprint (<c>CredentialPurpose.WebAuthnChallenge</c>),
/// never the handle. The options, challenge included, are not secret: they travel to the
/// browser in the answer, and the check compares the response with exactly what was
/// offered.
/// </para>
/// <para>
/// One conditional statement consumes it (unused, unexpired, of the purpose expected and,
/// for a registration, of the caller), BEFORE the library checks the response: every
/// failure spends it, so a response can never be retried against the same challenge.
/// </para>
/// </remarks>
public sealed class WebAuthnChallenge : EntityBase
{
    /// <summary>How long a challenge may be answered: the ceremony timeout offered to the browser.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private WebAuthnChallenge()
    {
    }

    /// <summary>Constructor for Dapper mapping.</summary>
    public WebAuthnChallenge(
        Guid id,
        string handleHash,
        WebAuthnChallengePurpose purpose,
        Guid? userId,
        string optionsJson,
        DateTime expiresAt,
        DateTime? usedAt,
        DateTime createdAt) : base(id)
    {
        HandleHash = handleHash;
        Purpose = purpose;
        UserId = userId;
        OptionsJson = optionsJson;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the keyed fingerprint of the handle the browser holds.</summary>
    public string HandleHash { get; private set; } = string.Empty;

    /// <summary>Gets the ceremony it was issued for.</summary>
    public WebAuthnChallengePurpose Purpose { get; private set; }

    /// <summary>Gets the account a registration challenge belongs to; null for an assertion.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Gets the options offered, as the JSON the browser received.</summary>
    public string OptionsJson { get; private set; } = string.Empty;

    /// <summary>Gets when it stops being accepted (UTC).</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>Gets when it was spent (UTC), or null.</summary>
    public DateTime? UsedAt { get; private set; }

    /// <summary>Gets when it was issued (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// A registration challenge for a signed-in account.
    /// </summary>
    /// <param name="userId">The account adding a passkey.</param>
    /// <param name="handleHash">The fingerprint of the handle the cookie carries.</param>
    /// <param name="optionsJson">The options offered.</param>
    /// <param name="issuedAt">Now (UTC), from the caller's clock.</param>
    public static WebAuthnChallenge IssueForRegistration(
        Guid userId,
        string handleHash,
        string optionsJson,
        DateTime issuedAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(handleHash);
        ArgumentException.ThrowIfNullOrEmpty(optionsJson);

        return new WebAuthnChallenge
        {
            HandleHash = handleHash,
            Purpose = WebAuthnChallengePurpose.Registration,
            UserId = userId,
            OptionsJson = optionsJson,
            ExpiresAt = issuedAt.Add(Lifetime),
            CreatedAt = issuedAt,
        };
    }
}
