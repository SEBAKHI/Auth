namespace Auth_Lib.Application.DTOs;

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
    public bool IsActive { get; set; }
    public bool AllowSelfRegistration { get; set; }
    public bool RequireTwoFactor { get; set; }
    public bool RequireEmailVerification { get; set; }
    public int SessionTimeoutMinutes { get; set; }
    public int MaxConcurrentSessions { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
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
