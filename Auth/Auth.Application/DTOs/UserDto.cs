using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for user information.
/// </summary>
public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>Absolute URL of the user's profile image (composed from the stored key); null when unset.</summary>
    public string? ProfileImageUrl { get; set; }

    public UserStatus Status { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? TimeZone { get; set; }
    public string? Theme { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Security / account fields
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public string? LastLoginIp { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public DateTime? PasswordExpiresUtc { get; set; }
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }

    /// <summary>Display name of the creating user; null when unresolved.</summary>
    public string? CreatedByName { get; set; }

    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }

    /// <summary>Display name of the last modifying user; null when unresolved.</summary>
    public string? ModifiedByName { get; set; }

    /// <summary>Whether the account is soft-deleted. Only surfaced to callers who requested deleted accounts.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp of the soft deletion; null while the account is live.</summary>
    public DateTime? DeletedAt { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];
}

/// <summary>
/// Paginated result for users.
/// </summary>
public class PagedUsersDto
{
    public IReadOnlyList<UserDto> Users { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
