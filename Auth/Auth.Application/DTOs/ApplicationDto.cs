using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for application information.
/// </summary>
public class ApplicationDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Whether the application is switched on. Off means nobody signs in,
    /// whatever <see cref="AccessMode"/> says. Changed through the dedicated
    /// activate/deactivate endpoints, never through an update.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Who may sign in while the application is on: everyone on the platform,
    /// or only individually invited users.
    /// </summary>
    public ApplicationAccessMode AccessMode { get; set; }

    public bool AllowSelfRegistration { get; set; }
    public bool RequireTwoFactor { get; set; }
    public bool RequireEmailVerification { get; set; }
    public int SessionTimeoutMinutes { get; set; }
    public int MaxConcurrentSessions { get; set; }

    /// <summary>
    /// Step-up re-authentication threshold in minutes. When set, an OAuth
    /// authorize request for this app is only honored if the user signed in
    /// within this window; an older SSO session forces a fresh login. Null
    /// (the default) disables step-up.
    /// </summary>
    public int? ReauthenticationMaxAgeMinutes { get; set; }

    /// <summary>
    /// Exact-match allowlist of OAuth redirect URIs (authorization-code flow).
    /// Populated on single-application reads; empty in paged lists.
    /// </summary>
    public List<string> RedirectUris { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }

    /// <summary>Display name of the creating user; null when unresolved.</summary>
    public string? CreatedByName { get; set; }

    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }

    /// <summary>Display name of the last modifying user; null when unresolved.</summary>
    public string? ModifiedByName { get; set; }
}

/// <summary>
/// Paginated result for applications.
/// </summary>
public class PagedApplicationsDto
{
    public IReadOnlyList<ApplicationDto> Applications { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
