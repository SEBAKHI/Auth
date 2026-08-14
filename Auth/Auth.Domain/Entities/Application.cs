using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents an application registered in the authentication system.
/// Applications provide SSO support and scope isolation for roles/permissions.
/// </summary>
public class Application : AggregateRoot
{
    /// <summary>
    /// Gets the unique application code (e.g., "CRM", "ERP").
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
    /// Gets whether the application is switched on. This is the stronger of the
    /// two access switches and beats everything: a deactivated application
    /// admits nobody — not invited users, not organization members, not
    /// platform administrators — and no token issued for it can be refreshed.
    /// <see cref="AccessMode"/> is only consulted once this is true.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets who may sign in while the application is active. Independent of
    /// <see cref="IsActive"/>: this decides the audience, that decides whether
    /// there is an audience at all. New applications start
    /// <see cref="ApplicationAccessMode.Restricted"/>.
    /// </summary>
    public ApplicationAccessMode AccessMode { get; private set; }

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
    /// Stored, never enforced. No sign-in path reads this value: sessions are
    /// capped per user across the whole platform by
    /// <c>Session:MaxConcurrentSessions</c>, applied in
    /// <c>LoginResponseBuilder.BuildAsync</c>. Counting per application would
    /// also be close to meaningless today — only the OAuth token endpoint sets
    /// <see cref="UserSession.ApplicationId"/>, so every other sign-in leaves it
    /// null and would fall outside any per-application count.
    /// <para>
    /// Kept because the column, the API contract and any integration reading
    /// them are older than that decision; removing it would be a breaking change
    /// buying nothing. The console no longer offers it as a control, which is
    /// what actually mattered: it was showing operators a limit that did not
    /// exist.
    /// </para>
    /// </summary>
    public int MaxConcurrentSessions { get; private set; }

    /// <summary>
    /// Step-up authentication: when set, an authorization request for this app
    /// requires the user to have signed in within this many minutes; an older
    /// SSO session is not accepted and the user must re-authenticate. Null (the
    /// default) disables step-up — the SSO session is honored for its full life.
    /// </summary>
    public int? ReauthenticationMaxAgeMinutes { get; private set; }

    /// <summary>
    /// Gets whether the application has been soft-deleted. Deleted applications
    /// are excluded from operational queries and their credentials are rejected.
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// Gets when the application was soft-deleted.
    /// </summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    /// Gets the user who soft-deleted the application.
    /// </summary>
    public Guid? DeletedBy { get; private set; }

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
        Guid? modifiedBy,
        ApplicationAccessMode accessMode) : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        BaseUrl = baseUrl;
        LogoUrl = logoUrl;
        ContactEmail = contactEmail;
        IsActive = isActive;
        AccessMode = accessMode;
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

    /// <summary>
    /// Creates a registration. The application is switched on immediately but
    /// starts <see cref="ApplicationAccessMode.Restricted"/>: nobody can sign in
    /// until an administrator invites them, or opens the application to everyone.
    /// Born-open was the previous behavior and is the defect this default fixes.
    /// </summary>
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
            AccessMode = ApplicationAccessMode.Restricted,
            AllowSelfRegistration = false,
            RequireTwoFactor = false,
            RequireEmailVerification = false,
            SessionTimeoutMinutes = 60,
            MaxConcurrentSessions = 5
        };
        application.SetCreated(createdBy);
        return application;
    }

    /// <summary>
    /// Updates the editable settings. Deliberately does NOT touch
    /// <see cref="IsActive"/> — switching an application off is its own command
    /// (<see cref="Activate"/> / <see cref="Deactivate"/>) so a full-object PUT
    /// built from stale client state can never silently switch one back on.
    /// </summary>
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
        ApplicationAccessMode accessMode,
        Guid modifiedBy,
        int? reauthenticationMaxAgeMinutes = null)
    {
        Name = name;
        Description = description;
        BaseUrl = baseUrl;
        LogoUrl = logoUrl;
        ContactEmail = contactEmail;
        AccessMode = accessMode;
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

    /// <summary>
    /// Switches the application on. Does not change who may sign in — that is
    /// <see cref="AccessMode"/>, which this deliberately leaves alone so an
    /// application reactivated after an incident comes back with the same
    /// audience it had when it was switched off.
    /// </summary>
    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Switches the application off for everyone. Callers are expected to also
    /// revoke the application's refresh tokens and terminate its sessions;
    /// already-issued access tokens survive until they expire on their own.
    /// </summary>
    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Soft-deletes the application: the record is kept for referential
    /// integrity and history, but it is deactivated, excluded from operational
    /// queries, and its credentials stop validating.
    /// </summary>
    public void Delete(Guid deletedBy)
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
        IsActive = false;
        SetModified(deletedBy);
    }

    /// <summary>
    /// Hydrates the deletion state from persistence without touching audit
    /// fields. For repository use only.
    /// </summary>
    public void LoadDeletionState(bool isDeleted, DateTime? deletedAt, Guid? deletedBy)
    {
        IsDeleted = isDeleted;
        DeletedAt = deletedAt;
        DeletedBy = deletedBy;
    }
}
