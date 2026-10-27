using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Entities;

/// <summary>
/// A pending OAuth 2.0 authorize request, kept on the server while the browser
/// passes through an accounts page (sign-in, registration or organization
/// creation). The page carries only the client id and an opaque 256-bit handle;
/// only the HMAC-SHA256 hash of the handle is stored, never the handle itself.
/// </summary>
/// <remarks>
/// The accounts pages used to carry the whole authorize URL, and a web
/// application firewall reads a parameter holding an off-site <c>https://</c>
/// address as a remote file inclusion (OI-145). The values here are the ones
/// the authorize handler validated, never the raw query string; a resume runs
/// every check again on them.
/// <para>
/// The expiry is fixed when the row is created and never slides. A row is not
/// deleted when it is used: the authorize URL it replaces could always be
/// replayed (the back button re-authorizes), and deleting it would turn a
/// double navigation into a false "expired".
/// </para>
/// </remarks>
public class AuthorizationRequest : EntityBase
{
    /// <summary>
    /// Gets the HMAC-SHA256 hash of the handle, for lookups.
    /// </summary>
    public string HandleHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ID of the application (OAuth client) the request names.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    /// Gets the <c>client_id</c> exactly as the client sent it. A resume must
    /// present the same value.
    /// </summary>
    public string ClientId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the <c>response_type</c>.
    /// </summary>
    public string ResponseType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the <c>redirect_uri</c>, a URI registered for the application.
    /// </summary>
    public string RedirectUri { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the PKCE <c>code_challenge</c>.
    /// </summary>
    public string CodeChallenge { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the PKCE <c>code_challenge_method</c>.
    /// </summary>
    public string CodeChallengeMethod { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the relying party's <c>state</c>, verbatim.
    /// </summary>
    public string? State { get; private set; }

    /// <summary>
    /// Gets the OIDC <c>prompt</c> as the recognised values in canonical order,
    /// or null when the request carried none.
    /// </summary>
    public string? Prompt { get; private set; }

    /// <summary>
    /// Gets the OIDC <c>max_age</c> in seconds, or null when the request carried
    /// none (a malformed value counts as none, as it does at authorize).
    /// </summary>
    public int? MaxAge { get; private set; }

    /// <summary>
    /// Gets the requested <c>scope</c> set in canonical text.
    /// </summary>
    public string Scope { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the request asks for an organization (<c>create_organization=true</c>).
    /// </summary>
    public bool CreateOrganization { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the request was stored.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp after which the handle no longer resumes anything.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the language and colour mode the relying party asked the accounts
    /// pages of this request to show in, resolved; <see cref="PresentationHints.None"/>
    /// when it named neither.
    /// </summary>
    public PresentationHints Hints { get; private set; } = PresentationHints.None;

    private AuthorizationRequest() : base()
    {
    }

    /// <summary>
    /// Rehydrates a stored request.
    /// </summary>
    public AuthorizationRequest(
        Guid id,
        string handleHash,
        Guid applicationId,
        string clientId,
        string responseType,
        string redirectUri,
        string codeChallenge,
        string codeChallengeMethod,
        string? state,
        string? prompt,
        int? maxAge,
        string scope,
        bool createOrganization,
        DateTime createdAt,
        DateTime expiresAt,
        PresentationHints? hints = null) : base(id)
    {
        Hints = hints ?? PresentationHints.None;
        HandleHash = handleHash;
        ApplicationId = applicationId;
        ClientId = clientId;
        ResponseType = responseType;
        RedirectUri = redirectUri;
        CodeChallenge = codeChallenge;
        CodeChallengeMethod = codeChallengeMethod;
        State = state;
        Prompt = prompt;
        MaxAge = maxAge;
        Scope = scope;
        CreateOrganization = createOrganization;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Stores a request that has passed every check of the authorize endpoint.
    /// </summary>
    /// <remarks>
    /// The arguments are the handler's validated values, so nothing a client
    /// sends can fail here: an empty identifier or a non-positive lifetime is a
    /// programming error and throws.
    /// </remarks>
    public static AuthorizationRequest Create(
        string handleHash,
        Guid applicationId,
        string clientId,
        string responseType,
        string redirectUri,
        string codeChallenge,
        string codeChallengeMethod,
        string? state,
        string? prompt,
        int? maxAge,
        string scope,
        bool createOrganization,
        TimeSpan lifetime,
        DateTime nowUtc,
        PresentationHints? hints = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handleHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        return new AuthorizationRequest
        {
            HandleHash = handleHash,
            ApplicationId = applicationId,
            ClientId = clientId,
            ResponseType = responseType,
            RedirectUri = redirectUri,
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = codeChallengeMethod,
            State = state,
            Prompt = prompt,
            MaxAge = maxAge,
            Scope = scope,
            CreateOrganization = createOrganization,
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc.Add(lifetime),
            Hints = hints ?? PresentationHints.None
        };
    }

    /// <summary>
    /// Whether the handle still resumes the request at <paramref name="nowUtc"/>.
    /// </summary>
    public bool IsLive(DateTime nowUtc) => nowUtc < ExpiresAt;

    /// <summary>
    /// Whether <paramref name="clientId"/> is the client this request was made by.
    /// </summary>
    /// <remarks>
    /// Ordinal, against the value as sent: the accounts page shows the client the
    /// URL names, so a client id edited in the address bar must not resume
    /// another client's request.
    /// </remarks>
    public bool IsFor(string? clientId) => string.Equals(ClientId, clientId, StringComparison.Ordinal);

    /// <summary>
    /// Withdraws the request for an organization ("continue without an
    /// organization"). Permanent for this request, as rewriting the authorize URL
    /// was for that flow.
    /// </summary>
    public void DropOrganizationRequest() => CreateOrganization = false;
}
