using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents an application registered in the authentication system.
/// Applications provide SSO support and scope isolation for roles/permissions.
/// </summary>
public class Application : AggregateRoot
{
    /// <summary>
    /// Gets the unique application code (e.g., "AUTH", "CRM", "ERP").
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the display name of the application.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the description of the application.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the base URL where the application is hosted.
    /// </summary>
    public string? BaseUrl { get; private set; }

    /// <summary>
    /// Gets the URL of the application logo.
    /// </summary>
    public string? LogoUrl { get; private set; }

    /// <summary>
    /// Gets the contact email for the application.
    /// </summary>
    public string? ContactEmail { get; private set; }

    /// <summary>
    /// Gets whether the application is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets whether the application allows self-registration.
    /// </summary>
    public bool AllowSelfRegistration { get; private set; }

    /// <summary>
    /// Gets whether the application requires two-factor authentication.
    /// </summary>
    public bool RequireTwoFactor { get; private set; }

    /// <summary>
    /// Gets whether the application requires email verification before login.
    /// </summary>
    public bool RequireEmailVerification { get; private set; }

    /// <summary>
    /// Gets the session timeout in minutes.
    /// </summary>
    public int SessionTimeoutMinutes { get; private set; }

    /// <summary>
    /// Gets the maximum number of concurrent sessions allowed.
    /// </summary>
    public int MaxConcurrentSessions { get; private set; }

    /// <summary>
    /// Step-up authentication: when set, an authorization request for this app
    /// requires the user to have signed in within this many minutes; an older
    /// SSO session is not accepted and the user must re-authenticate. Null (the
    /// default) disables step-up — the SSO session is honored for its full life.
    /// </summary>
    public int? ReauthenticationMaxAgeMinutes { get; private set; }

    private readonly List<string> _redirectUris = [];

    /// <summary>
    /// Gets the exact-match allowlist of OAuth redirect URIs for the
    /// authorization-code flow. The authorize endpoint only ever redirects to
    /// one of these values.
    /// </summary>
    public IReadOnlyList<string> RedirectUris => _redirectUris.AsReadOnly();

    private Application() : base()
    {
    }

    public Application(
        Guid id,
        string code,
        string name,
        string? description,
        string? baseUrl,
        string? logoUrl,
        string? contactEmail,
        bool isActive,
        bool allowSelfRegistration,
        bool requireTwoFactor,
        bool requireEmailVerification,
        int sessionTimeoutMinutes,
        int maxConcurrentSessions,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        BaseUrl = baseUrl;
        LogoUrl = logoUrl;
        ContactEmail = contactEmail;
        IsActive = isActive;
        AllowSelfRegistration = allowSelfRegistration;
        RequireTwoFactor = requireTwoFactor;
        RequireEmailVerification = requireEmailVerification;
        SessionTimeoutMinutes = sessionTimeoutMinutes;
        MaxConcurrentSessions = maxConcurrentSessions;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    public static Application Create(
        string code,
        string name,
        string? description,
        string? baseUrl,
        Guid createdBy)
    {
        var application = new Application
        {
            Code = code.ToUpperInvariant(),
            Name = name,
            Description = description,
            BaseUrl = baseUrl,
            IsActive = true,
            AllowSelfRegistration = false,
            RequireTwoFactor = false,
            RequireEmailVerification = false,
            SessionTimeoutMinutes = 60,
            MaxConcurrentSessions = 5
        };
        application.SetCreated(createdBy);
        return application;
    }

    public void Update(
        string name,
        string? description,
        string? baseUrl,
        string? logoUrl,
        string? contactEmail,
        bool allowSelfRegistration,
        bool requireTwoFactor,
        bool requireEmailVerification,
        int sessionTimeoutMinutes,
        int maxConcurrentSessions,
        Guid modifiedBy,
        int? reauthenticationMaxAgeMinutes = null)
    {
        Name = name;
        Description = description;
        BaseUrl = baseUrl;
        LogoUrl = logoUrl;
        ContactEmail = contactEmail;
        AllowSelfRegistration = allowSelfRegistration;
        RequireTwoFactor = requireTwoFactor;
        RequireEmailVerification = requireEmailVerification;
        SessionTimeoutMinutes = sessionTimeoutMinutes;
        MaxConcurrentSessions = maxConcurrentSessions;
        ReauthenticationMaxAgeMinutes = reauthenticationMaxAgeMinutes;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Hydrates the step-up max-age from persistence without touching audit
    /// fields. For repository use only.
    /// </summary>
    public void LoadReauthenticationMaxAge(int? minutes)
    {
        ReauthenticationMaxAgeMinutes = minutes;
    }

    /// <summary>
    /// Hydrates the redirect URI allowlist from persistence without touching
    /// audit fields. For repository use only.
    /// </summary>
    public void LoadRedirectUris(IEnumerable<string> uris)
    {
        _redirectUris.Clear();
        _redirectUris.AddRange(NormalizeRedirectUris(uris));
    }

    /// <summary>
    /// Replaces the redirect URI allowlist (trimmed, de-duplicated).
    /// URI format rules are enforced by command validation; the entity only
    /// guarantees a clean, duplicate-free list.
    /// </summary>
    public void SetRedirectUris(IEnumerable<string> uris, Guid modifiedBy)
    {
        _redirectUris.Clear();
        _redirectUris.AddRange(NormalizeRedirectUris(uris));
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Checks whether the given redirect URI is on the allowlist. Comparison is
    /// exact (ordinal) per OAuth 2.0 security best practice — no wildcard or
    /// prefix matching, ever.
    /// </summary>
    public bool IsRedirectUriAllowed(string redirectUri)
    {
        return _redirectUris.Contains(redirectUri, StringComparer.Ordinal);
    }

    private static IEnumerable<string> NormalizeRedirectUris(IEnumerable<string> uris)
    {
        return uris
            .Select(u => u?.Trim() ?? string.Empty)
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.Ordinal);
    }

    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetModified(modifiedBy);
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetModified(modifiedBy);
    }
}
